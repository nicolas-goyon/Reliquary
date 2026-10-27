import { useState } from 'react'

interface CardImageProps {
  src: string | undefined
  alt: string
  className?: string
}

/** Shows the image of a card from the Scryfall servers. When the image is missing, it shows the name in a frame. */
export function CardImage({ src, alt, className }: CardImageProps) {
  const [failed, setFailed] = useState(false)
  if (!src || failed) {
    return (
      <div className={`card-image card-image-missing ${className ?? ''}`} role="img" aria-label={alt}>
        <span>{alt}</span>
      </div>
    )
  }

  return <img className={`card-image ${className ?? ''}`} src={src} alt={alt} loading="lazy" decoding="async" onError={() => setFailed(true)} />
}
