# 素材生成记录

以下为现有素材的原始生成提示词，集中保留以便追溯。图标后来仅做颜色调整和圆角裁切，当前修改定义见 `shortcut-face.svg`，原始表情见 `shortcut-face-original.png`。

## flight-wing-prompt.txt

```text
Use case: stylized-concept
Asset type: modular transparent PNG angel-wing sprite for a desktop pet animation.
Primary request: exactly ONE isolated fully extended angel wing, with its compact attachment root at the LEFT side near mid-height, growing smoothly toward an elegant upper-right tip and fanning outward to the RIGHT. This is a separate wing accessory to be mirrored and attached behind an existing yellow character; do not draw any character.
Composition: square canvas around 1024x1024, entire wing inside the frame with generous transparent padding; root near x=8%, y=56%; upper-right tip around x=91%, y=20%; long lower primary feathers curve toward the right, filling a broad graceful fan. Single wing only, three-quarter gentle 3D depth, presented clearly side-on for animation around the root.
Style: polished high-resolution 3D animated film render, tactile soft layered ivory-white feathers, individual feather shafts and overlapping feather groups clearly readable at small sizes. Warm off-white highlights, subtle pale-gold rim warmth, natural soft gray shadows between feathers, gentle studio lighting.
Background: genuinely transparent alpha, clean edges, no opaque backdrop and no cast shadow behind the object.
Constraints: only one wing, no pair of wings, no body, no bird, no person, no dragon, no text, no watermark, no halo, no glow cloud, no particles, no circles, no ground, no black or white background. Keep the base compact so it can be hidden behind a character shoulder.
```

## idle-hd-reference-match-prompt.txt

```text
Tool: built-in imagegen
Reference: user's codex-clipboard-c6ebb053-7584-4aa6-ac31-82da3c31cb32.png
Output: idle-hd-reference-match.png
Transparent background: true

Image restoration and background removal of the ATTACHED REFERENCE ONLY. Extract and faithfully clarify the exact yellow 奶蛙 character seen in this screenshot. The user's instruction is to match THIS figure, especially its head/neck/belly proportions; previous free redraws were wrong. Do not use a generic Naiwa design. Keep the exact same left-facing three-quarter pose, head size, eye positions, hands on belly, leg lengths and foot angles. In this reference the round upper head flows into a fairly THICK continuous neck and sloping back, not a tiny separate ball head on a pinched swan neck. Match that upper silhouette precisely. Preserve the clearly visible green-rimmed round eye with black pupil and the partly visible far green eye at the front-left edge; there is NO brown mammal nose or beak. The head and upper neck should read as one smoothly connected rounded yellow form, matching the screenshot, while the abdomen remains round but not exaggerated beyond the reference. Reproduce the original reference's actual anatomy and closed-mouth expression without making it chibi, cute, baby-like or more stylized. Improve only sharpness, edge smoothness and fine 3D surface/hand/eye detail. Remove the snow, landscape and all environmental background. Output a clean high-resolution transparent RGBA PNG, square canvas with full figure entirely visible and ample transparent margin. Match the full figure's narrow/tall body outline and relative head-to-belly size directly from the reference. No shadows, props, halos, scenery, text or watermark. Treat this as a faithful high-resolution cutout/restoration of the provided figure, not an opportunity to redesign it.
```

## laugh-peak-expression-prompt.txt

```text
Tool: built-in imagegen
Reference: laugh-shared-body-expression.png
Output: laugh-peak-expression.png
Transparent background: true
Application uses only the face; all body pixels come from the idle master.

Edit only the laughing facial expression in this exact Naiwa desktop-pet image. Keep the character's head outline, camera, neck, body, belly, arms, hands, legs, feet, materials, square canvas and transparency exactly unchanged. The current expression looks like a quiet chuckle. Make the facial expression a stronger, uninhibited belly laugh: eyes squeezed shut into lively curved eyelids, mouth opened wider and noticeably taller with a dark warm mouth cavity, a neat row of white upper teeth and a visible tongue. The laugh should feel exuberant and silly, not sinister or aggressive. Fit the mouth within the existing head and upper-neck silhouette; do not enlarge the head, swell the jaw, turn the character to face front, alter body proportions or move the hands. No tilted-body redesign; pose motion will be controlled by the application. Only the upper face region will be used; the body pixels will be ignored. High-resolution transparent PNG, same image framing and foot positions. No background, no props, no shadows, no text or watermark.
```

## listen-thinking-prompt.txt

```text
Tool: built-in imagegen
Reference: user's second attachment, codex-clipboard-7dfa7918-e4ab-459a-89ca-de3264ec734d.jpg
Transparent background: true

Create a transparent PNG asset for the LISTENING state of a Windows desktop pet, based on the attached reference photo of 奶蛙 (Naiwa). The attached image is a character-and-pose reference, not a scene to reproduce. Faithfully preserve this specific recognizable yellow 奶蛙: smooth rounded pear-shaped yellow head and body, two large round green eyes with dark pupils, a small gently CLOSED straight mouth, cream belly, dark gray small hands. Crucial pose: just like the reference, one dark hand rests under the chin in a thoughtful, attentive listening gesture, the other forearm rests across the lower belly. Subtle head tilt, calm attentive eyes looking toward the viewer. Soft matte plush-like 3D material and gentle studio lighting. Make a clean standalone full-character cutout with a complete rounded seated lower-body silhouette and small tucked feet so it can stand alone as a desktop pet; preserve the reference's upper-body hand-on-chin pose and facial proportions. Single consistent character only, front three-quarter view. Square 1024 by 1024 canvas; center the character, full character entirely visible with ample transparent margins, feet aligned near 92% image height. This is a calm listening/thinking expression: mouth closed at all times, NO open mouth, NO laughing, NO exaggerated grin, NO hands clutching the belly. No table, no wall, no background, no text, no lettering, no watermark, no logos, no props, no ground plane or cast shadow. Background must be genuinely transparent.
```

## rest-exhale-mouth-prompt.txt

```text
Mode: built-in imagegen edit. Mouth-only exhale variant of rest-supine.png.
Output: rest-exhale-mouth.png. Runtime uses only a feathered mouth patch.

Use case: precise-object-edit / identity-preserve.
Edit target: the supplied transparent full-body resting yellow Naiwa sprite.
Change ONLY the mouth to an exhaling expression: its lips softly purse into a small clearly visible rounded O opening as it releases a long relieved sigh after work. A little projecting yellow lip rim with matching soft 3D shading, a modest dark mouth opening, relaxed cheeks. The O should be visibly open, not just another closed line. No teeth, no tongue, no smile, no laughter, no worried expression.
Keep the mouth centered at the existing mouth's center and follow the head's exact three-quarter perspective. Retain the original mouth's position on the face; no second mouth or residual smile crease.
Keep EVERYTHING ELSE exactly as supplied: identical full canvas/aspect ratio, same head position/size and green eyes, same lying pose, belly, limbs, camera, proportions, yellow material, lighting and transparent silhouette. Eyes stay OPEN because software controls the blink separately. Do not alter eyes/eyebrows/body/anatomy or redraw the pose. No air marks, text, watermark, ground or props. Preserve the original alpha. This is a mouth-expression variant for compositing only; not a new character.

Refinement:
Edit ONLY the mouth on this transparent Naiwa sprite. The current mouth protrudes too much like a nozzle. Make it a natural, subtle exhaling O: a small dark softly oval opening with a thin soft yellow lip edge that sits almost flush with the face, only a slight pucker as if a person is releasing a relaxed long sigh. No thick raised circular ring, no tube, no pacifier or suction cup. Retain the mouth center, approximate opening visibility and head perspective. Keep the eyes open and all other pixels/body pose, silhouette, proportions, material, lighting, framing and transparent alpha unchanged. Do not add any air marks or text. This is a surgical mouth refinement, not a new pose.
```

## rest-supine-prompt.txt

```text
Mode: built-in imagegen, reference-guided transparent sprite generation.
Output: rest-supine.png
References: three user-provided supine Naiwa images, plus idle-hd-reference-match.png for established identity/material/proportions.

Use case: identity-preserve.
Asset type: a single high-resolution transparent RGBA sprite for a Windows desktop pet, named 躺平奶蛙.
Input roles: images 1-3 are SUPINE POSE and anatomy references only, ignore every screenshot UI element, text, bed/room/grass/background. Image 4 is the EXISTING PET IDENTITY and material reference. Make that exact adult-proportioned yellow Naiwa character lie relaxed on its back, in the physical supine pose shown in references 1 and 2. Preserve the recognisable head size, integrated thick neck, rounded heavy cream-bellied torso, short thick legs, green-rimmed black eyes, olive-brown hands/feet. Do not redesign it as a cute chibi character or enlarge the head.
Composition: one complete isolated full-body character, head at upper left and feet at lower right. Low three-quarter view from the feet side, slightly above the body, visibly lying horizontally FACE UP, back and head resting on an implied flat surface. Body projection wider than tall. Both arms rest comfortably outward on the same implied surface, palms relaxed; legs naturally apart, feet relaxed. Heavy belly protrudes upwards under gravity. Neck and head rest down, do not sit up or recline against an invisible chair. Eyes naturally open, relaxed neutral/slightly tired closed-mouth expression, suitable for procedural eyelid animation. Character only, NO bed, NO pillow, NO blanket, NO floor plane, NO grass, NO ground shadow or scenery.
Match the clean softly lit golden-yellow 3D render and material of image 4, with natural subtle surface texture, sharp facial details, smooth clean silhouette and genuinely transparent background. Keep image 4's established character proportions, with a substantial abdomen and normal sized head. Whole body in frame with clear padding on every side. No text, watermarks, logos, UI, checkerboard artwork, montage, duplicate character, distortion, extra limbs. This should be a clean production-ready single pose, not an animation strip.
```

## shortcut-face-prompt.txt

```text
Use case: precise-object-edit
Asset type: square opaque desktop shortcut icon image, precise extraction from the supplied phone screenshot.
Input image: the attached screenshot is the edit target.
Primary request: extract ONLY the large central pale-yellow SQUARE face artwork, removing everything outside that original square: all phone status bars, dark gray/olive areas, playback controls, progress bar, and black margins. The final canvas must be exactly a square filled edge-to-edge by the original pale-yellow artwork.
Preserve the artwork as faithfully as a clean crop: same flat pale-yellow background, same two green eyes at the same relative positions and sizes, same asymmetry. The LEFT eye is a narrow sideways crescent/oval with a black pupil; the RIGHT eye is a large green circular eye with a white inner ring and solid black pupil. Preserve the short dark-brown near-horizontal mouth line below the eyes, including its rounded ends and slight irregularity. Keep the original face composition, proportions, flat colors, and generous yellow space. Do not center, enlarge, symmetrize, or redesign the face.
Style and invariants: original simple flat 2D graphic; NO 3D, no head outline/cutout, no body, no shading, no new texture, no gradients, no shadow, no frame, no rounded border, no halo, no extra elements, no text, no logo. Do not add or remove eye details. Opaque pale yellow background, not transparent. This is an exact-looking crop/cleanup of the reference square, not a new illustration.
```
