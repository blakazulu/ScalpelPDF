"""Scalpel logo generator (Pillow) -- dramatic edition.

Mark: a surgical scalpel slicing a document, the amber cutting edge
(#F2A93B, the app's Studio accent) glowing hot. Brushed-steel blade,
graphite handle, moody vignetted ground, light spill on the paper.
"""
import sys, os
from PIL import Image, ImageDraw, ImageFilter, ImageChops

SS = 6
U = 256
S = U * SS
def u(v): return int(round(v * SS))

# --- palette ---
BG_TOP   = (40, 40, 44);  BG_BOT  = (11, 11, 13)
PAGE_TOP = (250, 250, 248); PAGE_BOT = (228, 228, 224)
PAGE_EDGE= (196, 196, 194, 255)
FOLD_TOP = (222, 222, 218); FOLD_BOT = (200, 200, 196)
LINE_COL = (181, 181, 178, 255)
HAND_LO  = (16, 16, 18);   HAND_HI = (86, 86, 92)
HAND_SPEC= (168, 168, 176, 255)
BLADE_LO = (120, 127, 135); BLADE_HI = (246, 249, 253)
BLADE_SPINE = (96, 104, 112, 255)
AMBER    = (242, 169, 59)
AMBER_HI = (255, 230, 165, 255)

# scalpel transform
SC = 1.14          # scale up (more dominant)
ANGLE_BLADE = -37  # steeper, more dynamic slash

def sca(p):
    return (128 + (p[0]-128)*SC, 128 + (p[1]-128)*SC)

def quad(p0, p1, p2, n=56):
    out = []
    for i in range(n + 1):
        t = i / n; mt = 1 - t
        out.append((mt*mt*p0[0] + 2*mt*t*p1[0] + t*t*p2[0],
                    mt*mt*p0[1] + 2*mt*t*p1[1] + t*t*p2[1]))
    return out

def layer():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))

def grad(c0, c1, axis="v"):
    small = Image.new("RGB", (1, 256) if axis == "v" else (256, 1))
    px = small.load()
    for i in range(256):
        t = i / 255
        px[(0, i) if axis == "v" else (i, 0)] = (
            int(c0[0] + (c1[0]-c0[0])*t),
            int(c0[1] + (c1[1]-c0[1])*t),
            int(c0[2] + (c1[2]-c0[2])*t))
    return small.resize((S, S), Image.BILINEAR)

def grad_fill(lay, mask_fn, g):
    rgba = g.convert("RGBA")
    mask = Image.new("L", (S, S), 0)
    mask_fn(ImageDraw.Draw(mask))
    rgba.putalpha(mask)
    lay.alpha_composite(rgba)

def soft_glow(points, color, width, blur, alpha):
    g = layer()
    ImageDraw.Draw(g).line([(u(x), u(y)) for x, y in points],
                           fill=color + (alpha,), width=int(SS*width), joint="curve")
    return g.filter(ImageFilter.GaussianBlur(u(blur)))

def draw_page(base, g_page, g_fold, angle=-7):
    x0, y0, x1, y1, f = 84, 58, 172, 206, 28
    body = [(x0, y0), (x1 - f, y0), (x1, y0 + f), (x1, y1), (x0, y1)]
    sh = layer()
    ImageDraw.Draw(sh).polygon([(u(px) + u(6), u(py) + u(11)) for px, py in body], fill=(0, 0, 0, 150))
    sh = sh.filter(ImageFilter.GaussianBlur(u(7))).rotate(angle, resample=Image.BICUBIC, center=(S/2, S/2))
    base.alpha_composite(sh)

    lay = layer()
    grad_fill(lay, lambda d: d.polygon([(u(px), u(py)) for px, py in body], fill=255), g_page)
    d = ImageDraw.Draw(lay)
    d.line([(u(px), u(py)) for px, py in body] + [(u(x0), u(y0))], fill=PAGE_EDGE, width=max(1, SS))
    fold = [(x1 - f, y0), (x1 - f, y0 + f), (x1, y0 + f)]
    grad_fill(lay, lambda d: d.polygon([(u(px), u(py)) for px, py in fold], fill=255), g_fold)
    d.line([(u(x1 - f), u(y0)), (u(x1 - f), u(y0 + f)), (u(x1), u(y0 + f))], fill=PAGE_EDGE, width=max(1, SS))
    for ly in (150, 168, 186):
        d.rounded_rectangle([u(x0 + 13), u(ly), u(x1 - 13), u(ly + 5)], radius=u(2.5), fill=LINE_COL)
    lay = lay.rotate(angle, resample=Image.BICUBIC, center=(S/2, S/2))
    base.alpha_composite(lay)

def draw_scalpel(base, g_blade, g_handle, angle=ANGLE_BLADE):
    lay = layer()
    back = [sca((122, 112)), sca((127, 50))]
    edge = [sca(p) for p in quad((138, 38), (140, 82), (134, 112))]
    tip  = sca((138, 38))
    blade_outline = back + [tip] + edge + [sca((122, 112))]

    # HOT amber glow off the cutting edge -- two passes (wide soft + tight bright)
    lay.alpha_composite(soft_glow([ (p[0], p[1]) for p in edge ], AMBER, 12, 6, 150))
    lay.alpha_composite(soft_glow([ (p[0], p[1]) for p in edge ], AMBER, 5, 2.5, 210))

    # handle: gradient graphite + specular highlight
    h0, h1 = sca((120, 114)), sca((136, 240))
    grad_fill(lay, lambda d: d.rounded_rectangle([u(h0[0]), u(h0[1]), u(h1[0]), u(h1[1])], radius=u(8), fill=255), g_handle)
    d = ImageDraw.Draw(lay)
    sp0, sp1 = sca((124.5, 120)), sca((124.5, 234))
    d.line([(u(sp0[0]), u(sp0[1])), (u(sp1[0]), u(sp1[1]))], fill=HAND_SPEC, width=max(1, int(SS*0.9)))
    for gy in (152, 164, 176):
        a, b = sca((124, gy)), sca((132, gy))
        d.line([(u(a[0]), u(a[1])), (u(b[0]), u(b[1]))], fill=(0, 0, 0, 120), width=max(1, int(SS*0.8)))
    c0, c1 = sca((119, 108)), sca((137, 120))
    d.rounded_rectangle([u(c0[0]), u(c0[1]), u(c1[0]), u(c1[1])], radius=u(3), fill=BLADE_SPINE)

    # brushed-steel blade
    grad_fill(lay, lambda d: d.polygon([(u(px), u(py)) for px, py in blade_outline], fill=255), g_blade)
    d = ImageDraw.Draw(lay)
    d.line([(u(px), u(py)) for px, py in back], fill=BLADE_SPINE, width=max(1, int(SS*1.3)))
    d.line([(u(px), u(py)) for px, py in edge], fill=AMBER + (255,), width=max(1, int(SS*1.8)), joint="curve")

    # tip burst -- the blade biting in
    bl = layer(); bd = ImageDraw.Draw(bl)
    bd.ellipse([u(tip[0]-7), u(tip[1]-7), u(tip[0]+7), u(tip[1]+7)], fill=AMBER + (220,))
    bl = bl.filter(ImageFilter.GaussianBlur(u(3)))
    lay.alpha_composite(bl)
    d.ellipse([u(tip[0]-2.5), u(tip[1]-2.5), u(tip[0]+2.5), u(tip[1]+2.5)], fill=AMBER_HI)

    lay = lay.rotate(angle, resample=Image.BICUBIC, center=(S/2, S/2))
    base.alpha_composite(lay)

def vignette(base):
    vmask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(vmask).ellipse([u(-30), u(-10), u(286), u(300)], fill=255)
    vmask = vmask.filter(ImageFilter.GaussianBlur(u(40)))
    dark = Image.new("RGBA", (S, S), (0, 0, 0, 210))
    dark.putalpha(ImageChops.invert(vmask))
    base.alpha_composite(dark)

def render(dark_bg=False):
    g_page   = grad(PAGE_TOP, PAGE_BOT, "v")
    g_fold   = grad(FOLD_TOP, FOLD_BOT, "v")
    g_blade  = grad(BLADE_LO, BLADE_HI, "h")
    g_handle = grad(HAND_LO, HAND_HI, "h")
    base = layer()
    rnd = lambda d: d.rounded_rectangle([u(8), u(8), u(248), u(248)], radius=u(50), fill=255)
    if dark_bg:
        grad_fill(base, rnd, grad(BG_TOP, BG_BOT, "v"))
        # faint amber ambient glow behind the slash (upper area)
        amb = layer()
        ImageDraw.Draw(amb).ellipse([u(120), u(70), u(210), u(160)], fill=AMBER + (60,))
        base.alpha_composite(amb.filter(ImageFilter.GaussianBlur(u(22))))
    draw_page(base, g_page, g_fold)
    draw_scalpel(base, g_blade, g_handle)
    if dark_bg:
        vig = layer(); vignette(vig)
        # clip vignette to the rounded square
        clip = Image.new("L", (S, S), 0); rnd(ImageDraw.Draw(clip))
        vig.putalpha(ImageChops.multiply(vig.getchannel("A"), clip))
        base.alpha_composite(vig)
        # top inner highlight
        hi = layer(); ImageDraw.Draw(hi).rounded_rectangle([u(8), u(8), u(248), u(110)], radius=u(50), fill=(255, 255, 255, 14))
        base.alpha_composite(hi)
    return base

def downscale(img, size):
    return img.resize((size, size), Image.LANCZOS)

if __name__ == "__main__":
    downscale(render(False), 512).save("branding/preview_transparent.png")
    downscale(render(True), 512).save("branding/preview_darkbg.png")
    print("wrote previews")
    if "--export" in sys.argv:
        os.makedirs("branding/tiles", exist_ok=True)
        master_t, master_d = render(False), render(True)
        downscale(master_d, 256).save("branding/scalpel.ico",
                                      sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
        for name, sz in {"Square44x44Logo.png": 44, "Square71x71Logo.png": 71,
                         "Square150x150Logo.png": 150, "Square310x310Logo.png": 310,
                         "StoreLogo.png": 50}.items():
            downscale(master_t, sz).save("branding/tiles/" + name)
        # rectangular MSIX assets: transparent mark centred with padding
        def rect_asset(w, h, mark_frac=0.78):
            canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            m = int(min(w, h) * mark_frac)
            mk = downscale(master_t, m)
            canvas.alpha_composite(mk, ((w - m) // 2, (h - m) // 2))
            return canvas
        rect_asset(310, 150, 0.80).save("branding/tiles/Wide310x150Logo.png")
        rect_asset(620, 300, 0.55).save("branding/tiles/SplashScreen.png")
        downscale(master_t, 1024).save("branding/scalpel-1024.png")
        print("wrote ico + all tiles")
