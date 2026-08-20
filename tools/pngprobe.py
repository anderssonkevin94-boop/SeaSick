#!/usr/bin/env python3
"""Mean sRGB brightness of horizontal bands of a screenshot.

Written because judging "is the water too dark" by eye off a compressed PNG
is guesswork, and because the shading maths kept predicting ~36% grey where
the render was actually delivering ~9%. Pure stdlib, no PIL.

    python3 tools/pngprobe.py /tmp/seasick-sail-2.png

Prints the mean colour of four bands down the frame, in a column clear of the
HUD. Use it to compare the near water against the far water rather than
arguing about a screenshot.
"""
import zlib, struct, sys

def read_png(path):
    d = open(path,'rb').read()
    assert d[:8] == b'\x89PNG\r\n\x1a\n'
    pos = 8; idat = b''; w=h=bd=ct=None
    while pos < len(d):
        ln = struct.unpack('>I', d[pos:pos+4])[0]; typ = d[pos+4:pos+8]
        data = d[pos+8:pos+8+ln]; pos += 12+ln
        if typ==b'IHDR': w,h,bd,ct = struct.unpack('>IIBB', data[:10])
        elif typ==b'IDAT': idat += data
        elif typ==b'IEND': break
    raw = zlib.decompress(idat)
    ch = {0:1,2:3,3:1,4:2,6:4}[ct]
    bpp = ch*bd//8; stride = w*bpp
    out = bytearray(h*stride); prev = bytearray(stride)
    p = 0
    for y in range(h):
        f = raw[p]; p += 1
        line = bytearray(raw[p:p+stride]); p += stride
        if f==1:
            for i in range(bpp, stride): line[i] = (line[i]+line[i-bpp]) & 255
        elif f==2:
            for i in range(stride): line[i] = (line[i]+prev[i]) & 255
        elif f==3:
            for i in range(stride):
                a = line[i-bpp] if i>=bpp else 0
                line[i] = (line[i] + ((a+prev[i])>>1)) & 255
        elif f==4:
            for i in range(stride):
                a = line[i-bpp] if i>=bpp else 0
                b = prev[i]; c = prev[i-bpp] if i>=bpp else 0
                pp = a+b-c; pa=abs(pp-a); pb=abs(pp-b); pc=abs(pp-c)
                pr = a if (pa<=pb and pa<=pc) else (b if pb<=pc else c)
                line[i] = (line[i]+pr) & 255
        out[y*stride:(y+1)*stride] = line; prev = line
    return w,h,ch,bytes(out)

path = sys.argv[1]
w,h,ch,px = read_png(path)
print(f"{path}  {w}x{h} ch={ch}")
def avg(x0,x1,y0,y1):
    r=g=b=n=0
    for y in range(y0,y1,3):
        for x in range(x0,x1,3):
            i=(y*w+x)*ch
            r+=px[i]; g+=px[i+1]; b+=px[i+2]; n+=1
    return (r/n, g/n, b/n)
# column away from HUD
x0,x1 = int(w*0.28), int(w*0.42)
for name,y0,y1 in [("far water (above line)", int(h*0.30), int(h*0.36)),
                   ("just above line",        int(h*0.38), int(h*0.41)),
                   ("just below line",        int(h*0.44), int(h*0.47)),
                   ("near water (bottom)",    int(h*0.75), int(h*0.85))]:
    r,g,b = avg(x0,x1,y0,y1)
    print(f"  {name:24s} sRGB8 ({r:5.1f},{g:5.1f},{b:5.1f})   = {r/255*100:4.1f}% grey")
