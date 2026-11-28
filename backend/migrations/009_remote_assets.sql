-- المرحلة 9: أصول الهدايا والإطارات تُخزَّن خارج التطبيق (Cloudflare R2/CDN) وتُدار من لوحة المشرف.
-- المسارات نسبية إلى ASSETS_BASE_URL (أو رابط https كامل). لا أصول مضمّنة في APK.

ALTER TABLE gifts  ADD COLUMN icon_path   TEXT;
ALTER TABLE gifts  ADD COLUMN effect_path TEXT;
ALTER TABLE gifts  ADD COLUMN updated_at  TIMESTAMPTZ NOT NULL DEFAULT now();
ALTER TABLE frames ADD COLUMN asset_path  TEXT;
ALTER TABLE frames ADD COLUMN updated_at  TIMESTAMPTZ NOT NULL DEFAULT now();

UPDATE gifts  SET icon_path = 'gifts/bugatti.webp', effect_path = 'gifts/bugatti.svga' WHERE id = 'bugatti';
UPDATE frames SET asset_path = 'frames/vip6.svga' WHERE id = 'vip6';
