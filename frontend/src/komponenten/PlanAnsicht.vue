<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import L from 'leaflet'
import type { Plan, Punkt } from '../api'
import { formatZahl, nachAbsolut } from '../geometrie'

/** Punkt-Markierung im Plan (z.B. Nullpunkt, Messpunkt, später Positionen). */
export interface Markierung {
  id: string
  punkt: Punkt
  art: 'nullpunkt' | 'messpunkt' | 'position'
  text?: string
  verschiebbar?: boolean
  /** Klick auf die Markierung löst `markierungKlick` aus */
  klickbar?: boolean
  /** Hervorgehoben (z.B. ausgewählte Position) */
  aktiv?: boolean
  /** Noch nicht gespeichert */
  neu?: boolean
  /** Kleine Zahl am Punkt (z.B. offene Kommentare); 0/leer = keine */
  zaehler?: number
}

/** Linie zwischen zwei Planpunkten (z.B. Messstrecke oder Verbindung). */
export interface Linie {
  id: string
  von: Punkt
  bis: Punkt
  /** Dauerhaft angezeigter Text (Messstrecken) */
  text?: string
  /** mess = gestrichelte Hilfslinie (Vorgabe), verbindung = Verbindung zwischen Positionen */
  art?: 'mess' | 'verbindung' | 'weg'
  /** Hervorgehoben (z.B. Verbindungen der ausgewählten Position) */
  aktiv?: boolean
  /** Text beim Darüberfahren */
  hinweis?: string
}

const props = withDefaults(
  defineProps<{
    plan: Plan
    markierungen?: Markierung[]
    linien?: Linie[]
    /** Fadenkreuz-Cursor und Klick-Ereignisse aktiv */
    klickModus?: boolean
    /** Entsprechender Punkt zum Mauszeiger in einem anderen Plan (Kontrolle beim Referenzieren) */
    kontrollpunkt?: Punkt | null
    /** Überschrift oben in der Ansicht (z.B. «Bezugsplan: …») */
    titel?: string
  }>(),
  { markierungen: () => [], linien: () => [], klickModus: false, kontrollpunkt: null, titel: '' }
)

const emit = defineEmits<{
  klick: [punkt: Punkt]
  verschoben: [id: string, punkt: Punkt]
  markierungKlick: [id: string]
  /** Klick auf eine Verbindungslinie */
  linieKlick: [id: string]
  /** Mauszeiger über dem Planbild (null = ausserhalb) */
  zeiger: [punkt: Punkt | null]
}>()

const container = ref<HTMLDivElement | null>(null)
const zeiger = ref<Punkt | null>(null)
let karte: L.Map | null = null
let bild: L.ImageOverlay | null = null
// Leaflet merkt Grössenänderungen des Containers nicht selbst (z.B. Seitenleiste ein/aus)
let groessenBeobachter: ResizeObserver | null = null
const ebene = L.layerGroup()
// Eigener Marker, damit das Nachführen nicht alle anderen Markierungen neu zeichnet
let kontrollMarker: L.Marker | null = null

// Planeinheiten (u, v): Pixel des Planbilds, Ursprung oben links, v nach unten.
// Leaflet (CRS.Simple) zählt y nach oben, deshalb: LatLng(-v, u).
const nachLatLng = (p: Punkt) => L.latLng(-p.v, p.u)
const nachPunkt = (ll: L.LatLng): Punkt => ({ u: ll.lng, v: -ll.lat })

function planZeigen() {
  if (!karte) return
  bild?.remove()
  const { bildBreite: b, bildHoehe: h } = props.plan
  const grenzen = L.latLngBounds(nachLatLng({ u: 0, v: h }), nachLatLng({ u: b, v: 0 }))
  bild = L.imageOverlay(props.plan.bildUrl, grenzen).addTo(karte)
  karte.setMaxBounds(grenzen.pad(0.25))
  einpassen()
}

// True, solange der ganze Plan eingepasst ist und der Benutzer nicht gezoomt/verschoben hat
let eingepasst = false
let einpassenLaeuft = false

/** Ganzen Plan zeigen; herauszoomen ist bis eine Stufe weiter erlaubt */
function einpassen() {
  if (!bild || !karte) return
  const grenzen = bild.getBounds()
  einpassenLaeuft = true
  karte.setMinZoom(karte.getBoundsZoom(grenzen) - 1)
  karte.fitBounds(grenzen, { animate: false })
  einpassenLaeuft = false
  eingepasst = true
}

/**
 * Grösse der Fläche hat sich geändert (Seitenleiste, zweite Planansicht). Ist der ganze Plan
 * eingepasst, wird er neu eingepasst – sonst bleibt der gewählte Ausschnitt.
 */
function groesseGeaendert() {
  if (!karte || !bild) return
  karte.invalidateSize({ pan: false })
  if (eingepasst) einpassen()
}

// Texte werden als HTML eingefügt → immer maskieren (später stehen hier Benutzereingaben)
const maskiert = (text: string) =>
  text.replace(/[&<>"']/g, (z) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[z]!)

function symbol(m: Markierung) {
  const text = m.text ? `<span class="beschriftung">${maskiert(m.text)}</span>` : ''
  const zusatz = `${m.aktiv ? ' aktiv' : ''}${m.neu ? ' neu' : ''}`
  if (m.art === 'position') {
    return L.divIcon({
      className: `markierung markierung-position${zusatz}`,
      html: `<span class="punkt"></span>${text}${m.zaehler ? `<span class="zaehler" title="offene Kommentare">${m.zaehler}</span>` : ''}`,
      iconSize: [16, 16],
      iconAnchor: [8, 8]
    })
  }
  return L.divIcon({
    className: `markierung markierung-${m.art}${zusatz}`,
    html: `<span class="kreuz"></span>${text}`,
    iconSize: [28, 28],
    iconAnchor: [14, 14]
  })
}

function ebeneZeichnen() {
  ebene.clearLayers()
  for (const l of props.linien) {
    const verbindung = l.art === 'verbindung'
    const linie = L.polyline([nachLatLng(l.von), nachLatLng(l.bis)], l.art === 'weg'
      ? { color: '#2da44e', weight: 7, opacity: 0.9, interactive: !!l.hinweis }
      : verbindung
      ? { color: l.aktiv ? '#fb8500' : '#1b7c83', weight: l.aktiv ? 4 : 3, opacity: 0.85, interactive: !!l.hinweis }
      : { color: '#0969da', weight: 2, dashArray: '6 4', interactive: false }
    ).addTo(ebene)
    if (l.text) linie.bindTooltip(l.text, { permanent: true, direction: 'center', className: 'linien-text' })
    else if (l.hinweis) linie.bindTooltip(l.hinweis, { sticky: true, className: 'linien-hinweis' })
    if (verbindung && l.hinweis) linie.on('click', () => emit('linieKlick', l.id))
  }
  for (const m of props.markierungen) {
    const marker = L.marker(nachLatLng(m.punkt), {
      icon: symbol(m),
      draggable: !!m.verschiebbar,
      keyboard: false,
      title: m.verschiebbar ? 'Zum Verschieben ziehen' : m.text ?? '',
      zIndexOffset: m.aktiv || m.neu ? 1000 : 0
    }).addTo(ebene)
    if (m.verschiebbar) {
      marker.on('dragend', () => emit('verschoben', m.id, nachPunkt(marker.getLatLng())))
    }
    if (m.klickbar) {
      marker.on('click', () => emit('markierungKlick', m.id))
    }
  }
}

onMounted(() => {
  if (!container.value) return
  karte = L.map(container.value, {
    crs: L.CRS.Simple,
    minZoom: -6,
    maxZoom: 2, // höchstens 4-fach vergrössert; mehr zeigt nur noch Pixel
    zoomSnap: 0.25,
    zoomDelta: 0.5,
    wheelPxPerZoomLevel: 120, // feinere Schritte mit dem Mausrad
    attributionControl: false
  })
  ebene.addTo(karte)
  karte.on('zoomstart dragstart', () => {
    if (!einpassenLaeuft) eingepasst = false
  })
  karte.on('mousemove', (e: L.LeafletMouseEvent) => {
    const p = nachPunkt(e.latlng)
    const { bildBreite: b, bildHoehe: h } = props.plan
    zeiger.value = p.u >= 0 && p.v >= 0 && p.u <= b && p.v <= h ? p : null
    emit('zeiger', zeiger.value)
  })
  karte.on('mouseout', () => {
    zeiger.value = null
    emit('zeiger', null)
  })
  karte.on('click', (e: L.LeafletMouseEvent) => {
    if (!props.klickModus) return
    const p = nachPunkt(e.latlng)
    const { bildBreite: b, bildHoehe: h } = props.plan
    if (p.u >= 0 && p.v >= 0 && p.u <= b && p.v <= h) emit('klick', p)
  })
  planZeigen()
  ebeneZeichnen()
  kontrolleZeigen()
  groessenBeobachter = new ResizeObserver(groesseGeaendert)
  groessenBeobachter.observe(container.value)
})

function kontrolleZeigen() {
  if (!karte) return
  const p = props.kontrollpunkt
  if (!p) {
    kontrollMarker?.remove()
    kontrollMarker = null
    return
  }
  if (!kontrollMarker) {
    kontrollMarker = L.marker(nachLatLng(p), {
      icon: L.divIcon({ className: 'markierung markierung-kontrolle', html: '<span class="ring"></span>', iconSize: [22, 22], iconAnchor: [11, 11] }),
      interactive: false,
      keyboard: false,
      zIndexOffset: 2000
    }).addTo(karte)
  } else kontrollMarker.setLatLng(nachLatLng(p))
}

watch(() => props.plan.id, planZeigen)
watch(() => props.kontrollpunkt, kontrolleZeigen)
watch(() => [props.markierungen, props.linien], ebeneZeichnen, { deep: true })

onBeforeUnmount(() => {
  groessenBeobachter?.disconnect()
  karte?.remove()
  karte = null
})

function welt(p: Punkt) {
  return nachAbsolut(props.plan, p)
}

/** Plan auf einen Punkt zentrieren, mindestens auf Zoomstufe 0 (1:1) */
function zentrieren(p: Punkt) {
  if (!karte) return
  karte.setView(nachLatLng(p), Math.max(karte.getZoom(), 0))
}

defineExpose({ zentrieren })
</script>

<template>
  <div class="rahmen" :class="{ 'klick-modus': klickModus }">
    <div ref="container" class="karte-flaeche"></div>
    <div v-if="titel" class="titel">{{ titel }}</div>
    <button type="button" class="zweitrangig einpassen" title="Ganzen Plan anzeigen" @click="einpassen">
      Ganzer Plan
    </button>
    <div class="koordinaten" aria-live="off">
      <template v-if="zeiger">
        <template v-if="plan.referenziert">
          x {{ formatZahl(welt(zeiger).x) }} m · y {{ formatZahl(welt(zeiger).y) }} m
          <span class="px">(u {{ Math.round(zeiger.u) }} · v {{ Math.round(zeiger.v) }} px)</span>
        </template>
        <template v-else>u {{ Math.round(zeiger.u) }} · v {{ Math.round(zeiger.v) }} px</template>
      </template>
      <template v-else>Mausrad: zoomen · Ziehen: verschieben</template>
    </div>
  </div>
</template>

<style scoped>
.rahmen {
  position: relative;
}
.karte-flaeche {
  position: absolute;
  inset: 0;
  background: #e9ecef;
}
.klick-modus .karte-flaeche {
  cursor: crosshair;
}
.titel {
  position: absolute;
  top: 0.75rem;
  left: 3.5rem;
  z-index: 1000;
  pointer-events: none;
  max-width: calc(100% - 12rem);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  background: rgba(31, 35, 40, 0.85);
  color: #fff;
  border-radius: 6px;
  padding: 0.25rem 0.6rem;
  font-size: 0.85rem;
  font-weight: 600;
}
.einpassen {
  position: absolute;
  top: 0.75rem;
  right: 0.75rem;
  z-index: 1000;
  font-size: 0.9rem;
}
.koordinaten {
  position: absolute;
  left: 0.75rem;
  bottom: 0.75rem;
  z-index: 1000;
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid var(--rand);
  border-radius: 6px;
  padding: 0.25rem 0.6rem;
  font-size: 0.85rem;
  font-variant-numeric: tabular-nums;
  color: var(--text);
}
.px {
  color: var(--text-leise);
  margin-left: 0.25rem;
}
</style>

<style>
/* Leaflet-Symbole (nicht scoped, da Leaflet sie ausserhalb der Komponente erzeugt) */
.leaflet-container.leaflet-grab {
  cursor: inherit;
}
.markierung .kreuz {
  position: absolute;
  inset: 0;
}
.markierung .kreuz::before,
.markierung .kreuz::after {
  content: '';
  position: absolute;
  background: currentColor;
}
.markierung .kreuz::before {
  left: 13px;
  top: 0;
  width: 2px;
  height: 28px;
}
.markierung .kreuz::after {
  top: 13px;
  left: 0;
  height: 2px;
  width: 28px;
}
.markierung-nullpunkt {
  color: #cf222e;
}
.markierung-messpunkt {
  color: #0969da;
}
.markierung-kontrolle .ring {
  position: absolute;
  inset: 0;
  border-radius: 50%;
  border: 3px solid #fb8500;
  background: rgba(251, 133, 0, 0.18);
  box-shadow: 0 0 0 2px #fff;
}
.markierung-kontrolle .ring::after {
  content: '';
  position: absolute;
  left: 50%;
  top: 50%;
  width: 4px;
  height: 4px;
  margin: -2px 0 0 -2px;
  border-radius: 50%;
  background: #fb8500;
}
.markierung-position .punkt {
  position: absolute;
  inset: 2px;
  border-radius: 50%;
  background: #8250df;
  border: 2px solid #fff;
  box-shadow: 0 0 0 1px #8250df;
}
.markierung-position .beschriftung {
  left: 14px;
  top: -6px;
  color: #3e1f79;
}
.markierung-position .zaehler {
  position: absolute;
  left: -9px;
  top: -11px;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 8px;
  background: #cf222e;
  color: #fff;
  font: 700 10px/16px system-ui, sans-serif;
  text-align: center;
  border: 1px solid #fff;
  pointer-events: none;
}
.markierung-position.aktiv .punkt {
  background: #fb8500;
  box-shadow: 0 0 0 2px #fb8500, 0 0 0 5px rgba(251, 133, 0, 0.3);
}
.markierung-position.aktiv .beschriftung {
  color: #9a4b00;
}
.markierung-position.neu .punkt {
  background: #1a7f37;
  box-shadow: 0 0 0 2px #1a7f37, 0 0 0 5px rgba(26, 127, 55, 0.3);
}
.markierung-position .beschriftung,
.markierung-position .punkt {
  cursor: pointer;
}
.markierung .beschriftung {
  position: absolute;
  left: 18px;
  top: -14px;
  white-space: nowrap;
  font: 600 12px/1 system-ui, sans-serif;
  background: rgba(255, 255, 255, 0.9);
  padding: 2px 4px;
  border-radius: 3px;
}
.leaflet-marker-draggable {
  cursor: move;
}
.linien-text {
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid #0969da;
  color: #0550ae;
  font-weight: 600;
  box-shadow: none;
}
.linien-hinweis {
  font-weight: 600;
}
.linien-text::before {
  display: none;
}
</style>
