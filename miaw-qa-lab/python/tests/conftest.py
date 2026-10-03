"""Shared pytest setup for qalab.

YOU WRITE tasks: tests marked ``@pytest.mark.youwrite`` cover a function Sora writes himself
(or code that calls it). While that function is still a stub that raises NotImplementedError,
those tests count as expected failures (xfail), so CI stays green. Any other failure, such as
a wrong answer or a crash, still fails the run. When Sora's version passes review,
``/review-mine`` removes the marker.

    pytest python -m youwrite -rxX    # x = still open, X = passing (marker not removed yet)
"""

import pytest


def pytest_configure(config: pytest.Config) -> None:
    config.addinivalue_line(
        "markers", "youwrite: needs a function Sora writes himself (xfail while it's a stub)"
    )


def pytest_collection_modifyitems(items: list[pytest.Item]) -> None:
    # C# comparison: like adding an attribute to every test that has [Category("YouWrite")].
    for item in items:
        if item.get_closest_marker("youwrite"):
            item.add_marker(
                pytest.mark.xfail(
                    raises=NotImplementedError,
                    reason="YOU WRITE task still open",
                    strict=False,
                )
            )
