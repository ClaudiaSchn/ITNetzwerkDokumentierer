import { createRouter, createWebHistory } from 'vue-router'
import Startseite from './seiten/Startseite.vue'
import StandortSeite from './seiten/StandortSeite.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'start', component: Startseite },
    { path: '/standorte/:id(\\d+)', name: 'standort', component: StandortSeite, props: (r) => ({ id: Number(r.params.id) }) },
    { path: '/:pfad(.*)*', redirect: '/' }
  ]
})
