"""Reuse validated immutable geometry inside one owned movement session."""
from walk_geometry import Navigation


def execution_navigation(client,data):
    cached=getattr(client,'route_navigation',None)
    if cached is not None:
        return cached[1].fork()
    return Navigation(data,clearance=client.execution_clearance)
