# برومتات الذكاء الاصطناعي (إنجليزية، تعمل مع أغلب مولّدات الصور والفيديو)

مولّدات الفيديو لا تُخرج شفافية، فنطلب **خلفية مسطحة** ثم يحذفها المحوّل. الخطوات: (1) ولّد **صورة** بالبرومت الأول، (2) حوّلها لفيديو بالبرومت الثاني (صورة→فيديو)، (3) مرّر الفيديو على المحوّل.
تجنّب أسماء الماركات والشخصيات المحمية في البرومتات (بوغاتي وغيرها) كي لا تنتج أصولاً تخالف حقوق الملكية.

## قاعدة لكل برومت فيديو
أضف في نهايته: `static locked camera, no camera movement, no text, no watermark, seamless loop, 5 seconds`

---
## إطار VIP (خلفية خضراء، المفتاح green، --kind frame --hole 0.58)

**صورة:**
```
A perfectly circular ornate royal avatar frame, front view, perfectly centered and symmetrical, luxurious polished gold with inlaid red and blue gemstones and fine engraved details, thick ring. The center of the ring is empty and filled with the same flat solid pure green (#00FF00) as the background. Flat even lighting, no shadows on the background, no text, square 1:1.
```
**فيديو:**
```
The frame stays perfectly still and centered. A soft light sweep slowly glides around the gold ring, the gemstones twinkle, tiny golden sparkles float gently around the outside edge. Background and the center circle remain flat pure green (#00FF00) at all times. Seamless loop.
```
**تنويعات للصورة:** `...in violet crystal and silver with glowing blue energy runes` / `...in emerald and gold with small vine and leaf ornaments` / `...with large ornate golden wings spreading symmetrically left and right, staying inside the square canvas`.

---
## هدايا بتوهج وجسيمات (خلفية سوداء، المفتاح black، --kind gift)
ابدأ بصورة بنسبة 9:16 على أسود خالص، ثم الفيديو بنفس الفكرة.

**1) تاج وأمطار ذهب**
```
image: A glowing golden royal crown floating in the center, intense golden light rays, sparkling particles, on a pure black background, vertical 9:16, no text.
video: The crown spins slowly, burst of golden light, a shower of gold coins and sparkles falls from the top of the screen, light rays pulse, everything fades softly at the end. Pure black background throughout.
```
**2) صاروخ**
```
image: A sleek futuristic rocket with bright fire and smoke trail on a pure black background, vertical 9:16, glowing neon orange and blue.
video: The rocket launches upward from the bottom of the screen leaving a glowing trail, sparks and embers fly, a bright flash and fireworks burst at the top, fades out. Pure black background throughout.
```
**3) قلعة وألعاب نارية**
```
image: A glowing fantasy royal castle made of golden light, surrounded by sparkling stars, on a pure black background, vertical 9:16, no text.
video: The castle fades in with golden light, colorful fireworks explode around it in the dark sky, floating lanterns of light rise, then everything dissolves into sparkles. Pure black background throughout.
```
**4) أسد من نار**
```
image: A majestic lion head made of glowing golden fire and embers, roaring, on a pure black background, vertical 9:16, no text.
video: The fiery lion roars, a shockwave ring of golden light expands outward, embers swirl, then the fire dissolves into sparks. Pure black background throughout.
```

## هدية بجسم صلب (سيارة، مجوهرات…) → خلفية خضراء
بدّل `pure black background` بـ `flat solid pure green (#00FF00) background` وشغّل المحوّل بـ `--key green`. تجنّب أي لون أخضر في الجسم نفسه.
