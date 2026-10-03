"""Spec 02 §8: the template report. Works with no LLM and is the fallback when the LLM fails."""

from __future__ import annotations

from qalab.models.bug import (
    BugReport,
    Environment,
    EventRef,
    Frequency,
    Generator,
    Priority,
    Severity,
    Step,
)
from qalab.triage.cluster import Cluster
from qalab.triage.context import ClusterContext

TITLE_MAX = 100


def severity_from_priority(priority: Priority) -> Severity:
    """Template rule: without an LLM opinion, severity mirrors the computed priority."""
    return f"S{priority[1]}"  # type: ignore[return-value]


def template_component(cluster: Cluster) -> str:
    frame = cluster.top_frame
    if frame is not None:
        return frame.class_name
    if cluster.detector:
        return cluster.detector
    return cluster.scenes[0] if cluster.scenes else "unknown"


def template_title(cluster: Cluster) -> str:
    """``"{component}: {exception_type or detector} in {top frame}"``."""
    what = cluster.exception_type or cluster.detector or (cluster.level or cluster.kind)
    where = (
        cluster.top_frame.qualified
        if cluster.top_frame
        else (cluster.scenes[0] if cluster.scenes else "unknown location")
    )
    return f"{template_component(cluster)}: {what} in {where}"[:TITLE_MAX]


def template_steps(context: ClusterContext) -> list[Step]:
    steps = [
        Step(
            text=f"Step {a.step}: {a.action} {a.detail}".strip(),
            source="template",
            action_refs=[EventRef(run_id=a.run_id, seq=a.seq)],
        )
        for a in context.actions
    ]
    if not steps:
        steps.append(Step(text="Play the run with the recorded seed", source="template"))
    return steps


def base_fields(cluster: Cluster, context: ClusterContext, bug_id: str) -> dict:
    """Fields every report shares, whichever generator wrote the prose."""
    return {
        "id": bug_id,
        "signature": cluster.signature,
        "kind": cluster.kind,
        "priority": cluster.priority,
        "score": cluster.score,
        "score_breakdown": cluster.score_breakdown or None,
        "frequency": Frequency(
            count=cluster.count,
            runs=cluster.runs_affected,
            first_seen_t=cluster.first_t,
            last_seen_t=cluster.last_t,
        ),
        "environment": Environment(
            builds=context.facts.get("builds") or None,
            platforms=context.facts.get("platforms") or None,
            scenes=cluster.scenes or None,
        ),
        "attachments": context.screenshots or None,
    }


def template_report(
    cluster: Cluster,
    context: ClusterContext,
    bug_id: str,
    review_reasons: list[str] | None = None,
    generator: Generator | None = None,
) -> BugReport:
    reasons = list(review_reasons or [])
    first = context.events[0]
    summary = (
        f"{cluster.count} × {cluster.label} across {cluster.runs_affected} run(s); "
        f"first seen at t={cluster.first_t:g}s."
    )
    return BugReport(
        **base_fields(cluster, context, bug_id),
        title=template_title(cluster),
        component=template_component(cluster),
        severity=severity_from_priority(cluster.priority),
        summary=summary,
        steps_to_reproduce=template_steps(context),
        expected="unknown",
        actual=first.message,
        suspected_cause="unknown",
        evidence=[context.resolve(e.id) for e in context.events],
        docs_used=[d.chunk_id for d in context.docs] or None,
        confidence=0.0,
        needs_review=bool(reasons),
        review_reasons=reasons or None,
        generator=generator or Generator(method="template", attempts=1),
    )
