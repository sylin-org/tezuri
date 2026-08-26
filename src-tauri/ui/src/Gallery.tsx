// Gallery run: renders 2+ adjacent images as a photo-album group with
// lightbox. Natural sizes are probed once; layout is masonry-style rows.
// The document model stays dumb — Markdown keeps stacked images.

import { useEffect, useState } from "react";
import PhotoAlbum from "react-photo-album";
import Lightbox from "yet-another-react-lightbox";
import "yet-another-react-lightbox/styles.css";

interface Img { src: string; alt?: string }

export default function GalleryRun({ images }: { images: Img[] }) {
  const [dims, setDims] = useState<Record<string, { width: number; height: number }>>({});
  const [index, setIndex] = useState(-1);

  useEffect(() => {
    let live = true;
    images.forEach((img) => {
      if (dims[img.src]) return;
      const probe = new Image();
      probe.onload = () => {
        if (live) setDims((d) => ({ ...d, [img.src]: { width: probe.naturalWidth, height: probe.naturalHeight } }));
      };
      probe.src = img.src;
    });
    return () => { live = false; };
  }, [images]);

  const photos = images
    .filter((i) => dims[i.src])
    .map((i) => ({ src: i.src, alt: i.alt ?? "", ...dims[i.src] }));

  if (photos.length < 2) {
    // still measuring — render plain stack to avoid layout jump
    return (
      <div className="gallery-wrapper loading">
        {images.map((i, n) => <img key={n} src={i.src} alt={i.alt ?? ""} />)}
      </div>
    );
  }

  return (
    <div className="gallery-wrapper">
      <PhotoAlbum
        layout="rows"
        photos={photos}
        targetRowHeight={200}
        onClick={({ index: i }) => setIndex(i)}
        componentsProps={{ image: { style: { borderRadius: 6, cursor: "zoom-in" } } }}
      />
      <Lightbox open={index >= 0} index={Math.max(0, index)} close={() => setIndex(-1)} slides={photos} />
    </div>
  );
}
