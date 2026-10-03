"""Spec 02 §8: ask the LLM for a draft, validate it, retry, ground it, or fall back to the template.

The LLM writes prose and cites prompt ids. Python decides priority, resolves ids to events and
records every grounding problem in ``review_reasons`` so a QC lead knows what to double-check.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field

from pydantic import ValidationError

from qalab.llm.base import LLMError, LLMOutputError, LLMProvider, LLMResult
from qalab.models.bug import BugReport, EventRef, Generator, LLMBugDraft, Step
from qalab.models.schemas import load_schema
from qalab.triage.cluster import Cluster
from qalab.triage.context import ClusterContext
from qalab.triage.prompts import PromptTemplate, load_prompt
from qalab.triage.report_template import TITLE_MAX, base_fields, template_report

log = logging.getLogger(__name__)

DEFAULT_MAX_RETRIES = 2
MIN_CONFIDENCE = 0.5
RETRY_SUFFIX = "\n\nYour previous output failed validation: {error}. Return only valid JSON."


@dataclass
class DraftAttempts:
    """What the retry loop produced: the draft (or None) plus totals for ``generator``."""

    draft: LLMBugDraft | None = None
    attempts: int = 0
    tokens_in: int = 0
    tokens_out: int = 0
    latency_ms: float = 0.0
    cached: bool = False
    last_error: str | None = None
    results: list[LLMResult] = field(default_factory=list)


def request_draft(
    provider: LLMProvider,
    system: str,
    user: str,
    max_retries: int = DEFAULT_MAX_RETRIES,
    temperature: float = 0.0,
) -> DraftAttempts:
    """Call the model up to ``1 + max_retries`` times, feeding each validation error back."""
    schema = load_schema("llm_bug_draft")
    outcome = DraftAttempts()
    prompt = user
    for _ in range(1 + max_retries):
        outcome.attempts += 1
        try:
            result = provider.complete_json(system, prompt, schema, temperature=temperature)
        except LLMOutputError as exc:
            outcome.last_error = str(exc)
        except LLMError as exc:
            # Transport/server failure: retrying with an error message won't help.
            outcome.last_error = f"provider error: {exc}"
            break
        else:
            _account(outcome, result)
            try:
                outcome.draft = LLMBugDraft.model_validate(result.data)
                return outcome
            except ValidationError as exc:
                outcome.last_error = _short_validation_error(exc)
        log.info("draft attempt %d failed: %s", outcome.attempts, outcome.last_error)
        prompt = user + RETRY_SUFFIX.format(error=outcome.last_error)
    return outcome


def _account(outcome: DraftAttempts, result: LLMResult) -> None:
    outcome.results.append(result)
    outcome.tokens_in += result.tokens_in
    outcome.tokens_out += result.tokens_out
    outcome.latency_ms += result.latency_ms
    outcome.cached = result.cached


def _short_validation_error(exc: ValidationError) -> str:
    first = exc.errors()[0]
    where = ".".join(str(p) for p in first["loc"]) or "<root>"
    return f"{where}: {first['msg']}"


# ---- grounding ----------------------------------------------------------------------------


@dataclass
class Grounded:
    evidence: list[EventRef]
    steps: list[Step]
    docs_used: list[str]
    reasons: list[str]


def ground_draft(draft: LLMBugDraft, context: ClusterContext, cluster: Cluster) -> Grounded:
    """Apply every spec §8 grounding check; each failure adds one reason."""
    reasons: list[str] = []

    known = [i for i in draft.evidence_ids if i in context.event_ids]
    unknown = [i for i in draft.evidence_ids if i not in context.event_ids]
    if unknown:
        reasons.append(f"dropped unknown evidence ids: {', '.join(unknown)}")
    if not known:
        known = [context.events[0].id]
        reasons.append("no valid evidence ids in the draft; used E1")
    evidence = [context.resolve(i) for i in dict.fromkeys(known)]

    steps: list[Step] = []
    for number, step in enumerate(draft.steps_to_reproduce, start=1):
        bad = [i for i in step.action_ids if i not in context.action_ids]
        if step.source == "bot_log" and (bad or not step.action_ids):
            reasons.append(
                f"step {number} cited unknown action ids ({', '.join(bad) or 'none'}); "
                "marked inferred"
            )
            steps.append(Step(text=step.text, source="inferred"))
            continue
        refs = [context.resolve(i) for i in step.action_ids if i in context.action_ids]
        steps.append(Step(text=step.text, source=step.source, action_refs=refs or None))

    bad_docs = [i for i in draft.doc_ids if i not in context.doc_ids]
    if bad_docs:
        reasons.append(f"dropped unknown doc ids: {', '.join(bad_docs)}")
    docs_used = [context.doc_chunk_id(i) for i in draft.doc_ids if i in context.doc_ids]

    gap = abs(int(draft.severity[1]) - int(cluster.priority[1]))
    if gap > 1:
        reasons.append(
            f"LLM severity {draft.severity} disagrees with computed priority {cluster.priority}"
        )
    if draft.confidence < MIN_CONFIDENCE:
        reasons.append(f"low confidence ({draft.confidence:.2f})")
    reasons.extend(crash_reasons(context))

    return Grounded(evidence=evidence, steps=steps, docs_used=docs_used, reasons=reasons)


def crash_reasons(context: ClusterContext) -> list[str]:
    """Spec 00: the last error before an unclean exit is flagged as a possible crash."""
    if context.facts.get("last_before_crash"):
        return ["last error before the run ended without ended_at (possible crash)"]
    return []


# ---- entry point --------------------------------------------------------------------------


def generate_report(
    cluster: Cluster,
    context: ClusterContext,
    bug_id: str,
    provider: LLMProvider | None,
    max_retries: int = DEFAULT_MAX_RETRIES,
    temperature: float = 0.0,
    prompt: PromptTemplate | None = None,
) -> BugReport:
    """One cluster → one BugReport. ``provider=None`` means the template report."""
    if provider is None:
        return template_report(cluster, context, bug_id, crash_reasons(context))
    prompt = prompt or load_prompt("triage_v1")
    system = prompt.render_system()
    user = prompt.render_user(**context.to_prompt_vars())
    outcome = request_draft(provider, system, user, max_retries, temperature)
    generator = Generator(
        method="llm" if outcome.draft else "template",
        provider=provider.name,
        model=provider.model,
        prompt_version=prompt.version,
        latency_ms=outcome.latency_ms,
        tokens_in=outcome.tokens_in,
        tokens_out=outcome.tokens_out,
        cached=outcome.cached,
        attempts=outcome.attempts,
    )
    if outcome.draft is None:
        reason = f"LLM draft failed after {outcome.attempts} attempt(s): {outcome.last_error}"
        log.warning("%s: %s", bug_id, reason)
        return template_report(
            cluster, context, bug_id, [reason, *crash_reasons(context)], generator
        )
    return llm_report(outcome.draft, cluster, context, bug_id, generator)


def llm_report(
    draft: LLMBugDraft,
    cluster: Cluster,
    context: ClusterContext,
    bug_id: str,
    generator: Generator,
) -> BugReport:
    grounded = ground_draft(draft, context, cluster)
    return BugReport(
        **base_fields(cluster, context, bug_id),
        title=draft.title[:TITLE_MAX],
        component=draft.component,
        severity=draft.severity,
        summary=draft.summary,
        steps_to_reproduce=grounded.steps,
        expected=draft.expected,
        actual=draft.actual,
        suspected_cause=draft.suspected_cause,
        evidence=grounded.evidence,
        docs_used=grounded.docs_used or None,
        confidence=min(max(draft.confidence, 0.0), 1.0),
        needs_review=bool(grounded.reasons),
        review_reasons=grounded.reasons or None,
        generator=generator,
    )
