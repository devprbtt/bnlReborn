"""Shared process-relative milliseconds for the beta client clock and deadlines."""
import time

_ORIGIN = time.monotonic()

def millis():
    return int((time.monotonic() - _ORIGIN) * 1000)
