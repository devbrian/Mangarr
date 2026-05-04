// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/SeriesBanner.tsx (verbatim port).
//
// Manga sibling preserves: 35 / 70 px renderer choice + base64 placeholder.
// Manga sibling diverges from SeriesBanner: cover-type 'banner' resolves
// against `manga.images[]` per Lock #3.
//
// Phase 8 cleanup: collapse with SeriesBanner when Tv/ deletes.
import React from 'react';
import MangaImage, { MangaImageProps } from './MangaImage';

const bannerPlaceholder =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAXsAAABGCAIAAACiz6ObAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAYdEVYdFNvZnR3YXJlAHBhaW50Lm5ldCA0LjEuMWMqnEsAAAVeSURBVHhe7d3dduI4EEXheaMOfzPv/2ZzpCqLsmULQWjf1P4WkwnEtrhhr7IhnX9uAHAWigPgPBQHwHkoDoDzUBwA56E4AM5DcQCch+IAOA/FwQfuuonfA6ZRHLymuDwej3+r/zp6TI9rAxqElygODtXQ7CRmwNLj+wMdioMdas3uODOPkQe7KA5Wft+aiO5gg+LAfbc1DedZiCgOzF/JTaOD+zrIjeKguF6vmnE8D9+mlKloWsIXQ2IUByU3Rqc/HomviktQneQoTnaXy/Wi/xbfnXQ03eiAfuirL+QLIyWKk1oLQWhOic5XrunoIJvc+DK+ODKiOEmpBY9HuZpbaxByUOnxX0bHLhX74Zbpxuhx3r1Ki+IkZUGJXVAS+i5YPt5io83zsOuztrY00cmJ4mSkIlgdZBWdy/Xn51kHozTMjzuxNSbmRuKvTdTnglwoTkY2ZTS66z2ogdhEx+4oJZu9Gj2uKmmDuuHKj44VirMZmix2SIXipBMHnGZ9TWdbCrPct8M43dVD/cY6QJebnWDZTIQ8KE46R6OKBhBvQ51NdqMzQ3tp1z9/ygHsES26mW4axpxsKE4uuwNO086MajU+iY7vGHIjR7kxelL+5JAAxcnlaMAx+mnrhLVDo8pb0VFoSmxCbhS50ZK8aZUMxcnFX+XH4gVgi04fHD2iH+2WqH/8fn/xFjsnVqlQnETGp1Qmjjk91URTT7vZ2dNgBtKi46lKKE4qFCeR8fWUxt5+6pWTrHqe1d+OqqNF/aBDvGOVB8VJZLI49/CmVWPXdEz5pr91Hx2UmalKKE4eFCeRlyc45hE+EGjsZMpa037T7vaTzmTjuHicB8VJZLI48twzDQRXfL2/p2Hs/hQbruEoTiKTxRm/QlmIyqKEQrxbpmbvjtPF/aBDvGOVB8VJZLI4fvanp7gxDfVDDvGOVR4UJ5Hpyei8ON3fc8E5KE4ik8X5XnH8oKM4eVCcRCaL8/JyTnlQnDwoTiKTxXl2QbU8KE4eFCeRyeJoyWk5Mq8s5UFx8qA4iUx/Vmd5HmXi7P5GOMKzuh/JDSOlMjXLzTtUSi52Sp5KSnFwXjznzM6FUpzkpouz/L0WqnJWnFvZ1Vlx5kuBqPpVyR4UJ49PijP9mJL5jnPK7vT06f2g/+VBcQpoAS2gBbSAFtACWkALaAEtoAW0gBbQAlpAC2gBLaAFtIAW0AJaQAtoAS2gBbSAFtACWkALaAEtoAW0gBbQAlpA/99B/wd7kHH8CSaCpAAAAABJRU5ErkJggg==';

interface MangaBannerProps
  extends Omit<MangaImageProps, 'coverType' | 'placeholder'> {
  size?: 35 | 70;
}

function MangaBanner({ size = 70, ...otherProps }: MangaBannerProps) {
  return (
    <MangaImage
      {...otherProps}
      size={size}
      coverType="banner"
      placeholder={bannerPlaceholder}
    />
  );
}

export default MangaBanner;
