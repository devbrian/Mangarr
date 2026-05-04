// Sonarr divergence: NEW manga sibling per Phase 7 D-01 — see DIVERGENCE.md.
// Role-match analog: frontend/src/Series/SeriesImage.tsx (verbatim port).
//
// Manga sibling preserves: lazy-load + retry-on-error + size-suffix URL
// rewrite (Sonarr's MediaCover serves /MediaCover/{type}/{id}/poster.jpg
// AND /MediaCover/{type}/{id}/poster-{size}.jpg variants).
// Manga sibling diverges from SeriesImage:
//   * `coverType` may be 'cover' | 'screenshot' (manga MediaCover types)
//     in addition to 'poster' | 'banner' | 'fanart'.
//   * Imports CoverType / Image from Manga/Manga (re-exported aliases).
//
// Phase 8 cleanup: collapse with SeriesImage when Tv/ deletes.
import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import LazyLoad from 'react-lazyload';
import translate from 'Utilities/String/translate';
import { CoverType, Image } from './Manga';

function findImage(images: Image[], coverType: CoverType) {
  return images.find((image) => image.coverType === coverType);
}

function getUrl(image: Image, coverType: CoverType, size: number) {
  const imageUrl = image?.url;

  return imageUrl
    ? imageUrl.replace(`${coverType}.jpg`, `${coverType}-${size}.jpg`)
    : null;
}

export interface MangaImageProps {
  className?: string;
  style?: object;
  images: Image[];
  coverType: CoverType;
  placeholder: string;
  size?: number;
  lazy?: boolean;
  overflow?: boolean;
  title: string;
  onError?: () => void;
  onLoad?: () => void;
}

const pixelRatio = Math.max(Math.round(window.devicePixelRatio), 1);

function MangaImage({
  className,
  style,
  images,
  coverType,
  placeholder,
  size = 250,
  lazy = true,
  overflow = false,
  title,
  onError,
  onLoad,
}: MangaImageProps) {
  const [url, setUrl] = useState<string | null>(null);
  const [hasError, setHasError] = useState(false);
  const [isLoaded, setIsLoaded] = useState(true);
  const image = useRef<Image | null>(null);

  const alt = useMemo(() => {
    let type = translate('ImagePoster');

    switch (coverType) {
      case 'banner':
        type = translate('ImageBanner');
        break;
      case 'fanart':
        type = translate('ImageFanart');
        break;
      default:
        break;
    }

    return `${title} ${type}`;
  }, [title, coverType]);

  const handleLoad = useCallback(() => {
    setHasError(false);
    setIsLoaded(true);
    onLoad?.();
  }, [setHasError, setIsLoaded, onLoad]);

  const handleError = useCallback(() => {
    setHasError(true);
    setIsLoaded(false);
    onError?.();
  }, [setHasError, setIsLoaded, onError]);

  useEffect(() => {
    const nextImage = findImage(images, coverType);

    if (nextImage && (!image.current || nextImage.url !== image.current.url)) {
      image.current = nextImage;
      setUrl(getUrl(nextImage, coverType, pixelRatio * size));
      setHasError(false);
    } else if (!nextImage) {
      if (image.current) {
        image.current = null;
        setUrl(placeholder);
        setHasError(false);
        onError?.();
      }
    }
  }, [images, coverType, placeholder, size, onError]);

  useEffect(() => {
    if (!image.current) {
      onError?.();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (hasError || !url) {
    return <img className={className} style={style} src={placeholder} />;
  }

  if (lazy) {
    return (
      <LazyLoad
        height={size}
        offset={100}
        overflow={overflow}
        placeholder={
          <img className={className} style={style} src={placeholder} />
        }
      >
        <img
          alt={alt}
          className={className}
          style={style}
          src={url}
          rel="noreferrer"
          onError={handleError}
          onLoad={handleLoad}
        />
      </LazyLoad>
    );
  }

  return (
    <img
      alt={alt}
      className={className}
      style={style}
      src={isLoaded ? url : placeholder}
      onError={handleError}
      onLoad={handleLoad}
    />
  );
}

export default MangaImage;
