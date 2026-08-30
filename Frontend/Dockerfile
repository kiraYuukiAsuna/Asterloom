# syntax=docker/dockerfile:1

FROM node:24-bookworm-slim AS dependencies
WORKDIR /app

COPY Frontend/package.json Frontend/package-lock.json Frontend/.npmrc ./
RUN npm ci

FROM node:24-bookworm-slim AS build
WORKDIR /app

ENV NEXT_TELEMETRY_DISABLED=1

COPY --from=dependencies /app/node_modules ./node_modules
COPY Frontend/ ./
RUN npm run build

FROM node:24-bookworm-slim AS final
WORKDIR /app

ENV HOSTNAME=0.0.0.0 \
    NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000

EXPOSE 3000

# The runtime only needs the Node binary. Removing npm also keeps its build-time
# dependency tree (which is not used by the application) out of the final image.
RUN rm -rf /usr/local/lib/node_modules/npm \
    && rm -f /usr/local/bin/npm /usr/local/bin/npx

COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
COPY --from=build --chown=node:node /app/public ./public

USER node
CMD ["node", "server.js"]
