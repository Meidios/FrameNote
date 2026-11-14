"""Build the Windows icon pair and its proof sheet. Requires Python and Pillow."""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/FrameNote/Assets"
SIZES = (16, 20, 24, 32, 48, 64, 128, 256)
BLUE, TILE, PAPER, FOLD = "#A8DDFD", "#1F2227", "#22272F", "#D8EDFC"
AA = 4


def master(document=False):
    im = Image.new("RGBA", (1024*AA, 1024*AA))
    draw = ImageDraw.Draw(im)

    def box(bounds, radius, color):
        draw.rounded_rectangle(tuple(round(v*AA) for v in bounds),
                               radius=round(radius*AA), fill=color)

    def poly(points, color):
        draw.polygon([(round(x*AA), round(y*AA)) for x,y in points], fill=color)

    if document:
        # The folded corner is part of the silhouette; the canvas stays transparent.
        box((152,31,872,993), 72, PAPER)
        poly([(651,30),(873,30),(873,252)], (0,0,0,0))
        poly([(651,31),(872,252),(651,252)], FOLD)
    else:
        box((51,51,973,973), 922*.214, TILE)

    # One widescreen frame, with a note physically anchored to its upper edge.
    # Both members use precisely the same glyph geometry.
    scale = .83 if document else 1
    cx, cy = 512, 590 if document else 512

    def point(x,y):
        return (cx+x*scale, cy+y*scale)

    def rect(x1,y1,x2,y2,r,color):
        box((*point(x1,y1), *point(x2,y2)), r*scale, color)

    rect(-337,-222,337,222,65,BLUE)
    rect(-270,-155,270,155,22,PAPER if document else TILE)
    poly([point(x,y) for x,y in [(-9,-170),(176,-170),(176,86),(84,12),(-9,86)]], BLUE)
    return im.resize((1024,1024), Image.Resampling.LANCZOS)


def frame(original, size):
    im = original.resize((size,size), Image.Resampling.LANCZOS)
    if size <= 32:
        # Sharpen premultiplied channels to avoid a fringe on transparent edges.
        im = im.convert("RGBa").filter(ImageFilter.UnsharpMask(.55,115,2)).convert("RGBA")
    # Lanczos can leave a ringing alpha of 1–2 at a transparent canvas corner.
    for x in (0,size-1):
        for y in (0,size-1):
            im.putpixel((x,y),(0,0,0,0))
    return im


def proof(icons, dest):
    """Show the shipped frames at actual size, on both Windows surface tones."""
    sheet = Image.new("RGB", (806, 416), "#F4F5F6")
    pen = ImageDraw.Draw(sheet)
    def font(size):
        return ImageFont.truetype("segoeui.ttf", size)
    for row, (bg, fg, secondary) in enumerate([
            ("#F4F5F6","#20252A","#576574"), ("#282C32","#EEF1F5","#B6C0CB")]):
        y = row*208
        pen.rectangle((0,y,sheet.width-1,y+207),fill=bg)
        for x, name, caption in [(24,"FrameNote","FrameNote"),(411,"FrameNote_File",".framenote")]:
            with Image.open(icons[name]) as ico:
                for offset, size, dy in [(0,128,38),(143,64,71),(211,48,79),
                                         (264,32,87),(303,24,91),(332,20,93),(355,16,95)]:
                    tile=ico.ico.getimage((size,size))
                    sheet.paste(tile,(x+offset,y+dy),tile)
                pen.text((x+2,y+12),caption,font=font(18),fill=fg)
                for offset,size in [(0,128),(143,64),(211,48),(264,32),(303,24),(332,20),(355,16)]:
                    pen.text((x+offset+size/2,y+172),str(size),anchor="mt",font=font(11),fill=secondary)
    dest.parent.mkdir(parents=True,exist_ok=True)
    sheet.save(dest)


if __name__ == "__main__":
    ASSETS.mkdir(parents=True,exist_ok=True)
    paths = {}
    for name, document in [("FrameNote",False),("FrameNote_File",True)]:
        original = master(document)
        frames = [frame(original,n) for n in SIZES]
        dest = ASSETS / (name+".ico")
        frames[-1].save(dest,format="ICO",sizes=[(n,n) for n in SIZES],append_images=frames[:-1])
        paths[name] = dest
    proof(paths, ROOT/"docs/icons-preview.png")
