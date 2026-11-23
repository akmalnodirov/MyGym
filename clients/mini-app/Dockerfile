# The Mini App, built and then served by Caddy.
#
# Build context is this directory, not the repository root: the root .dockerignore excludes
# clients/ (correct for the API image, which has no business carrying the client's source), so a
# root-context build here would find nothing.
#
# The Caddyfile is deliberately NOT baked in. It lives at the repository root and is mounted by
# docker-compose, so reverse-proxy changes are a restart rather than a rebuild.

FROM node:22-alpine AS build
WORKDIR /app

# Dependencies first, so a source-only change does not reinstall them.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# `npm run build` is `tsc --noEmit && vite build`, so a type error fails the image rather than
# shipping a broken bundle. VITE_API_BASE_URL is left at its default of "/api" because the client
# and the API share one origin — see the Caddyfile.
RUN npm run build

FROM caddy:2-alpine
COPY --from=build /app/dist /srv
