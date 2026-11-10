"""Export the approved photographic plate into three independently moving UI textures."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


HERE = Path(__file__).resolve().parent
MASTERS = HERE / "masters"
SOURCE_SIZE = (1859, 846)
CANVAS = (1680, 720)
# The approved wide preview takes the central 797 source rows.
WIDE_BOX = (0, 24, 1859, 821)
LANCZOS = Image.Resampling.LANCZOS


def silhouette(points, cutouts=()):
    """Antialias source-space paths before projecting to the approved 21:9 crop."""
    scale = 3
    mask = Image.new("L", (SOURCE_SIZE[0] * scale, SOURCE_SIZE[1] * scale))
    draw = ImageDraw.Draw(mask)
    for path in points:
        draw.polygon([(round(x * scale), round(y * scale)) for x, y in path], fill=255)
    for path in cutouts:
        draw.polygon([(round(x * scale), round(y * scale)) for x, y in path], fill=0)
    return mask.resize(CANVAS, LANCZOS, box=tuple(v * scale for v in WIDE_BOX))


def oriented_seam(approved, clean, anchors, start, end):
    """Follow the irregular stone rim within a narrow band around traced landmarks."""
    xs = np.arange(start, end + 1)
    guide = np.interp(xs, *np.transpose(anchors))
    # The clean donor closely matches the background above the rim. Inside the wall the two
    # exposures differ. Track that transition continuously rather than treating highlights as edges.
    dist = np.abs(approved.astype(float) - clean.astype(float)).mean(axis=2)
    candidates = np.rint(guide).astype(int)[:, None] + np.arange(-6, 7)
    col = xs[:, None]
    before = sum(dist[candidates - d, col] for d in range(1, 4)) / 3
    after = sum(dist[candidates + d, col] for d in range(3)) / 3
    scores = after - before - np.abs(candidates - guide[:, None]) * 2
    scores = np.clip(scores, -80, 80)
    cost = scores[0]
    previous = np.zeros_like(scores, dtype=np.int8)
    for n in range(1, len(xs)):
        steps = np.abs(candidates[n - 1, :, None] - candidates[n, None, :])
        options = cost[:, None] - 4 * steps - 1000 * (steps > 2)
        previous[n] = np.argmax(options, axis=0)
        cost = scores[n] + options.max(axis=0)
    pos = int(np.argmax(cost))
    ys = []
    for n in range(len(xs) - 1, -1, -1):
        ys.append(int(candidates[n, pos]))
        pos = previous[n, pos]
    return list(zip(xs.tolist(), reversed(ys)))


def wall_mask(raw, course):
    guide = [(0,562),(60,560),(115,557),(176,553),(231,552),(301,549),(374,546),(451,543),
             (494,541),(539,540),(624,535),(704,532),(799,526),(872,524),(958,519),(1060,515),
             (1152,510),(1227,506),(1318,501),(1383,497),(1451,493),(1513,489),(1555,487),(1603,492)]
    edge = oriented_seam(raw, course, guide, 0, 1603)
    contour = edge + [(1608,510),(1610,540),(1607,620),(1604,753),(1568,760),(1514,762),
                      (1450,766),(1370,770),(1320,776),(1223,788),(1130,797),(1070,803),
                      (998,810),(899,822),(812,833),(741,845),(700,846),(0,846)]
    # The two lifting eyes are open rings in front of different parts of the course.
    left = [(489,545),(489,535),(490,526),(494,518),(501,513),(510,511),(519,512),(526,514),
            (532,519),(535,528),(536,542),(529,544),(490,547)]
    left_hole = [(498,539),(500,530),(503,524),(509,522),(517,521),(524,524),(527,529),(528,539)]
    right = [(1475,494),(1476,482),(1479,475),(1484,470),(1490,469),(1496,470),(1500,474),
             (1503,482),(1504,490),(1499,494)]
    right_hole = [(1481,489),(1482,482),(1485,476),(1489,475),(1494,476),(1497,481),(1497,489)]
    return silhouette([contour, left, right], [left_hole, right_hole])


def near_mask():
    # Paths follow the visible sandbags, the target sheet and each tube separately. Background is
    # visible between the feet and through the open handle. The recessed frame is on the same plane
    # as the portable stand; the distant targets are part of the fixed course.
    sacks = [(1349,631),(1354,624),(1363,619),(1370,610),(1380,599),(1397,589),(1410,581),
             (1428,571),(1452,561),(1473,555),(1500,549),(1527,547),(1551,547),(1575,549),
             (1594,553),(1614,560),(1606,616),(1605,631),(1600,653),(1596,684),(1590,707),
             (1584,737),(1576,766),(1562,776),(1542,783),(1522,790),(1502,798),(1486,805),
             (1475,801),(1454,799),(1428,795),(1403,791),(1381,786),(1353,775),(1346,772),
             (1351,766),(1357,763),(1362,749),(1373,726),(1388,710),(1373,705),(1361,696),
             (1358,691),(1364,683),(1370,681),(1375,673),(1383,663),(1397,653),(1381,649),
             (1363,644),(1357,635)]
    plate = [(1699,416),(1737,414),(1741,419),(1733,467),(1748,471),(1757,486),
             (1758,496),(1733,625),(1714,734),(1630,712),(1622,646),(1632,585),(1647,525),(1656,495),
             (1672,476),(1682,471),(1694,420)]
    outer_left = [(1571,780),(1574,757),(1576,741),(1581,716),(1583,692),(1590,655),
                  (1595,632),(1602,591),(1613,535),(1623,477),(1632,428),(1638,397),
                  (1642,385),(1648,378),(1655,374),(1667,372),(1772,372),(1783,372),
                  (1793,377),(1800,386),(1803,398),(1800,420),(1782,511),(1766,594),
                  (1750,671),(1739,741),(1728,824),(1709,822),(1723,746),(1736,676),
                  (1750,595),(1765,506),(1779,419),(1783,398),(1780,390),(1776,387),
                  (1769,386),(1665,387),(1658,390),(1654,396),(1649,415),(1643,446),
                  (1629,528),(1616,606),(1603,678),(1590,751),(1588,786)]
    inner_left = [(1596,779),(1605,721),(1615,656),(1630,567),(1643,497),(1656,415),
                  (1662,398),(1668,391),(1675,387),(1690,385),(1696,390),(1683,394),
                  (1676,402),(1672,414),(1665,455),(1653,520),(1641,589),(1628,668),
                  (1611,783)]
    inner_right = [(1752,819),(1756,784),(1767,703),(1777,628),(1789,533),(1798,464),
                   (1807,407),(1805,395),(1796,384),(1801,382),(1810,388),(1814,396),
                   (1817,406),(1814,426),(1805,497),(1793,588),(1780,683),(1767,818)]
    lower_cross = [(1583,747),(1587,745),(1719,775),(1757,782),(1755,799),(1715,789),
                   (1583,761)]
    mid_cross = [(1600,651),(1607,649),(1729,674),(1731,683),(1723,688),(1600,665)]
    chain = [(1731,385),(1737,385),(1734,396),(1729,414),(1726,415)]
    return silhouette([sacks, plate, outer_left, inner_left, inner_right, lower_cross, mid_cross, chain])


def split(visible, donor, matte):
    """Solve visible = coverage * cutout + (1 - coverage) * underlying at subpixel edges.

    A narrow repair margin removes traces of discontinuous rims, cast/contact shadows and fine
    metal hardware outside the main silhouette. Inside it, recover enough fractional coverage
    for the observed color to be representable with physically bounded straight RGB.
    """
    v = np.asarray(visible, dtype=np.float32)
    d = np.asarray(donor, dtype=np.float32)
    a = np.asarray(matte, dtype=np.float32)[:, :, None] / 255
    support = matte.point(lambda x: 255 if x >= 32 else 0).filter(ImageFilter.MaxFilter(19))
    # Carry the approved local exposure and existing soft shadows across the repair boundary.
    # The repair images supply the hidden material/geometry. Outside the repair margin use the
    # approved image exactly, never a second model interpretation of the visible course.
    protect = 1 - np.asarray(support.filter(ImageFilter.MaxFilter(5)), dtype=np.float32) / 255
    def blurred(array):
        return np.asarray(Image.fromarray(np.clip(array, 0, 255).astype("uint8")).filter(
            ImageFilter.GaussianBlur(9)), dtype=np.float32)
    weight = blurred(protect * 255)[:, :, None] / 255
    adjust = np.stack([blurred(127 + (v[:, :, c] - d[:, :, c]) * protect) - 127 for c in range(3)], axis=2)
    d = np.clip(d + np.clip(adjust / np.maximum(weight, .18), -65, 65), 0, 255)
    repair = np.asarray(support.filter(ImageFilter.GaussianBlur(2)), dtype=np.float32)[:, :, None] / 255
    under = np.rint(v * (1 - repair) + d * repair)
    required = np.maximum((v - under) / np.maximum(255 - under, 1),
                          (under - v) / np.maximum(under, 1)).max(axis=2, keepdims=True)
    a = np.ceil(255 * np.maximum(a, np.clip(1.7 * required, 0, 1))) / 255
    inverse = 1.0 - a
    cut = np.clip((v - inverse * under) / np.maximum(a, 1e-6), 0, 255)
    rgba = Image.fromarray(np.rint(cut).astype("uint8"))
    rgba.putalpha(Image.fromarray(np.rint(255 * a[:, :, 0]).astype("uint8")))
    return Image.fromarray(np.rint(under).astype("uint8")), rgba


def load_source(name):
    image = Image.open(MASTERS / name).convert("RGB")
    if image.size != SOURCE_SIZE:
        raise ValueError(f"Misregistered source {name}: {image.size}")
    return image


def project(image):
    return image.resize(CANVAS, LANCZOS, box=WIDE_BOX)


def compile_art(output):
    output.mkdir(parents=True, exist_ok=True)
    approved_path = MASTERS / "streiflicht-approved-21x9.jpg"
    approved = Image.open(approved_path).convert("RGB")
    if approved.size != CANVAS:
        raise ValueError("Approved wide crop has changed dimensions.")
    raw = load_source("streiflicht-source.png")
    course = load_source("course-repair.png")
    wall = load_source("wall-repair.png")
    under, front = split(approved, project(wall), near_mask())
    backing, mid = split(under, project(course), wall_mask(np.asarray(raw), np.asarray(course)))
    # Maximum quality and full chroma retain the approved color and texture under fine silhouettes.
    backing.save(output / "hall.jpg", quality=98, subsampling=0, optimize=True)
    mid.save(output / "barrier.png", optimize=True)
    front.save(output / "fence.png", optimize=True)
    shutil.copyfile(approved_path, output / "splash.jpg")


def verify(output, previews):
    approved = Image.open(MASTERS / "streiflicht-approved-21x9.jpg").convert("RGB")
    back = Image.open(output / "hall.jpg").convert("RGBA")
    mid = Image.open(output / "barrier.png").convert("RGBA")
    front = Image.open(output / "fence.png").convert("RGBA")
    rest = Image.alpha_composite(Image.alpha_composite(back, mid), front).convert("RGB")
    error = np.abs(np.asarray(rest, dtype=np.int16) - np.asarray(approved, dtype=np.int16))
    print(f"Rest vs approved JPEG, RGB values 0..255: mean={error.mean():.3f} p99={np.percentile(error, 99):.0f} max={error.max()}")
    if error.mean() > 0.7 or np.percentile(error, 99) > 4:
        raise RuntimeError("Rest composition no longer reproduces the approved photograph.")
    if previews is None:
        return
    previews.mkdir(parents=True, exist_ok=True)
    metrics = {"source": "streiflicht-approved-21x9.jpg", "size": list(CANVAS),
               "meanAbsoluteRgbError": float(error.mean()), "p99AbsoluteRgbError": int(np.percentile(error,99)),
               "maxAbsoluteRgbError": int(error.max()),
               "sha256": {f: hashlib.sha256((output / f).read_bytes()).hexdigest()
                          for f in ("hall.jpg", "barrier.png", "fence.png", "splash.jpg")}}
    (previews / "artwork-check.json").write_text(json.dumps(metrics, indent=2) + "\n", encoding="utf-8")
    # These are asset-space offsets bounding both native aspect ratios at the four pointer corners.
    for name, sign_x, sign_y in (("rest",0,0),("tl",-1,-1),("tr",1,-1),("bl",-1,1),("br",1,1)):
        frame = back.copy()
        frame.alpha_composite(mid, (4 * sign_x, 4 * sign_y))
        frame.alpha_composite(front, (8 * sign_x, 8 * sign_y))
        frame = frame.convert("RGB")
        for label, aspect in (("21x9", 21/9), ("16x9", 16/9)):
            # Native panel covers the viewport plus 13/1080 of its height on each side for travel.
            h = CANVAS[1]/(1+26/1080)
            w = h*aspect
            l,t=(CANVAS[0]-w)/2,(CANVAS[1]-h)/2
            crop = frame.resize((round(h*aspect),round(h)), LANCZOS, box=(l,t,l+w,t+h))
            crop.save(previews / f"streiflicht-{label}-{name}.jpg", quality=93)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--previews", type=Path)
    args = parser.parse_args()
    compile_art(args.output)
    verify(args.output, args.previews)
