<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import L from 'leaflet'
import type { Plan } from '../api'

const props = defineProps<{ plan: Plan }>()

const container = ref<HTMLDivElement | null>(null)
const zeiger = ref<{ u: number; v: number } | null>(null)
let karte: L.Map | null = null
let bild: L.ImageOverlay | null = null

// Planeinheiten (u, v) sind Pixel des Planbilds, Ursprung oben links, v nach unten.
// Leaflet (CRS.Simple) zählt y nach oben, deshalb: LatLng(-v, u).
const nachLatLng = (u: number, v: number) => L.latLng(-v, u)

function planZeigen() {
  if (!karte) return
  bild?.remove()
  const { bildBreite: b, bildHoehe: h } = props.plan
  const grenzen = L.latLngBounds(nachLatLng(0, h), nachLatLng(b, 0))
  bild = L.imageOverlay(props.plan.bildUrl, grenzen).addTo(karte)
  karte.setMaxBounds(grenzen.pad(0.25))
  karte.fitBounds(grenzen)
  karte.setMinZoom(karte.getZoom() - 1)
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
  karte.on('mousemove', (e: L.LeafletMouseEvent) => {
    const { bildBreite: b, bildHoehe: h } = props.plan
    const u = Math.round(e.latlng.lng)
    const v = Math.round(-e.latlng.lat)
    zeiger.value = u >= 0 && v >= 0 && u <= b && v <= h ? { u, v } : null
  })
  karte.on('mouseout', () => (zeiger.value = null))
  planZeigen()
})

watch(() => props.plan.id, planZeigen)

onBeforeUnmount(() => {
  karte?.remove()
  karte = null
})

function einpassen() {
  if (bild && karte) karte.fitBounds(bild.getBounds())
}
</script>

<template>
  <div class="rahmen">
    <div ref="container" class="karte-flaeche"></div>
    <button type="button" class="zweitrangig einpassen" title="Ganzen Plan anzeigen" @click="einpassen">
      Ganzer Plan
    </button>
    <div class="koordinaten" aria-live="off">
      <template v-if="zeiger">u {{ zeiger.u }} · v {{ zeiger.v }} px</template>
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
  color: var(--text-leise);
}
</style>
