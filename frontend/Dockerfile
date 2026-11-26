# ---- Build stage: turn the React/TypeScript code into plain HTML/JS/CSS ----
FROM node:22-alpine AS build
WORKDIR /app

# Install packages first (cached, so rebuilds are faster when only code changes)
COPY package.json package-lock.json ./
RUN npm ci

# Copy the rest of the code and build it into /app/dist
COPY . .
RUN npm run build

# ---- Run stage: a tiny web server that serves the built files ----
FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
