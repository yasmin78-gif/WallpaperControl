import struct,hashlib,json,sys
from pathlib import Path
def inspect(path):
    data=Path(path).read_bytes();u16=lambda p:struct.unpack_from('<H',data,p)[0];u32=lambda p:struct.unpack_from('<I',data,p)[0]
    pe=u32(0x3c);opt=pe+24;secs=opt+u16(pe+20)
    def off(rva):
        for i in range(u16(pe+6)):
            s=secs+i*40
            if u32(s+12)<=rva<u32(s+12)+max(u32(s+8),u32(s+16)):return u32(s+20)+rva-u32(s+12)
        return rva
    def string(rva):
        p=off(rva);return data[p:data.index(0,p)].decode('ascii')
    imports=[];imp=u32(opt+120)
    if imp:
        p=off(imp)
        while any(data[p:p+20]):imports.append(string(u32(p+12)));p+=20
    exports=[];er=u32(opt+112)
    if er:
        e=off(er);t=off(u32(e+32));exports=[string(u32(t+i*4)) for i in range(u32(e+24))]
    sections=[]
    for i in range(u16(pe+6)):
        p=secs+i*40;sections.append(dict(name=data[p:p+8].rstrip(b'\0').decode('ascii'),virtualSize=u32(p+8),rawSize=u32(p+16)))
    return dict(file=Path(path).name,size=len(data),sha256=hashlib.sha256(data).hexdigest().upper(),machine=hex(u16(pe+4)),imports=imports,exports=exports,sections=sections,peTimestamp=u32(pe+8))
if __name__=='__main__':print(json.dumps([inspect(p) for p in sys.argv[1:]],indent=2))
