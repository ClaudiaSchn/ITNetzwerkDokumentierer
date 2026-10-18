# Frontend bauen
FROM node:22-alpine AS build
WORKDIR /app
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# Caddy mit fertigem Frontend
FROM caddy:2.10-alpine
COPY web/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/dist /srv
