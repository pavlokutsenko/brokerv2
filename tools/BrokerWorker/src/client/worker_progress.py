"""Per-job progress; never publish partial broker data as a complete result."""
import json
import os
from datetime import datetime, timezone
from pathlib import Path


def check_stop():
    value = os.environ.get('PRICECHECK_STOP_FILE')
    if value and Path(value).exists():
        raise InterruptedError('Collection stopped')


def publish(detail, **metrics):
    value = os.environ.get('PRICECHECK_PROGRESS_FILE')
    if not value:
        return
    path = Path(value)
    temporary = path.with_suffix(f'.{os.getpid()}.tmp')
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary.write_text(json.dumps({'detail': detail,
            'at': datetime.now(timezone.utc).isoformat(), **metrics}), encoding='utf-8')
        temporary.replace(path)
    except OSError:
        # Reporting failure must not skip normal hook restoration.
        temporary.unlink(missing_ok=True)
