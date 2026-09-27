"""Bounded read-only reflected lobby metadata discovery, never a gameplay call."""
import argparse,json
from pathlib import Path
from passby_event_collector import Memory,Lu4MemoryClient,pointer
from inspect_shop_ufunctions import decode_name,read_object

def main():
    ap=argparse.ArgumentParser();ap.add_argument('pid',type=int);ap.add_argument('--globals',type=Path,required=True);ap.add_argument('--json',type=Path,required=True)
    args=ap.parse_args();g=json.loads(args.globals.read_text());out=[]
    with Lu4MemoryClient() as client:
        mem=Memory(client,args.pid);objects=g['gobjects_candidates'][0]['address'];names=g['fname_pool_candidates'][0]['address']
        for index in (19208,19214):
            function=read_object(mem,objects,index);owner=mem.u64(function+0x20)
            for obj,label in ((function,'function'),(owner,'class')):
                fields=[];field=mem.u64(obj+0x50);seen=set()
                while pointer(field) and field not in seen and len(fields)<80:
                    seen.add(field)
                    try: raw=mem.read(field,160).hex()
                    except OSError:
                        fields.append({'field':hex(field),'error':'unreadable'});break
                    fields.append({'field':hex(field),'name':decode_name(mem,names,mem.i32(field+0x20)),
                        'bytes':raw})
                    field=mem.u64(field+0x18)
                out.append({'index':index,'kind':label,'object':hex(obj),'name':decode_name(mem,names,mem.i32(obj+0x18)),
                    'fields':fields,'bytes':mem.read(obj,224).hex()})
    args.json.write_text(json.dumps(out,indent=2));print(json.dumps(out))

if __name__=='__main__':main()
