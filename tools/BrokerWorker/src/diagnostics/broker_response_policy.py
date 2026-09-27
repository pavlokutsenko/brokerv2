"""Retain complete, correlated broker answers without retrying an incomplete epoch."""


def validated_responses(fresh, expected):
    answers = {}
    warnings = []
    for response in fresh:
        pair = (int(response['arg0']), int(response['arg1']))
        if pair not in expected:
            warnings.append(f'unexpected response item={pair[0]} type={pair[1]}')
            continue
        if pair in answers:
            warnings.append(f'duplicate response item={pair[0]} type={pair[1]}')
            continue
        if int(response['copied_count']) != int(response['count']):
            warnings.append(f'truncated response item={pair[0]} '
                            f"rows={response['copied_count']}/{response['count']}")
            continue
        if len(response['rows']) != int(response['count']):
            warnings.append(f'inconsistent decoded rows item={pair[0]}')
            continue
        answers[pair] = response
    missing = expected - answers.keys()
    if missing:
        warnings.append(f'missing complete answers={len(missing)}; received={len(answers)}/{len(expected)}')
    return list(answers.values()), warnings
