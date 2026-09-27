"""Rebuild Giran over the complete saved scene extent, without live memory reads."""
import hashlib
import json
from pathlib import Path
from project_market import build as sections
from build_navigation_map import build as navigation


def main():
    root=Path(__file__).resolve().parents[2]/'maps/Giran'
    source=root/'giran-market-2026-09-24-scene.json'
    raw_bytes=source.read_bytes();raw=json.loads(raw_bytes)
    projected=sections(raw,raw['extent'])
    result=navigation(raw,projected)
    result['source_scene_sha256']=hashlib.sha256(raw_bytes).hexdigest()
    result['origin']=[80000,147000]
    result['rebuilt_at']='2026-09-26'
    result['coverage_note']='Full saved scene extent; missing ground remains unknown, not invented.'
    for filename,data in [('giran-market-2026-09-26-expanded-data.json',projected),
                           ('giran-navigation-2026-09-26-expanded.json',result)]:
        (root/filename).write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    print(json.dumps({'extent':raw['extent'],'obstacles':len(result['obstacles']),
                      'unknown':len(result['unknown']),'ground_cells':len(result['ground']['values']),
                      'missing_ground':sum(z is None for z in result['ground']['values'])}))


if __name__=='__main__': main()
