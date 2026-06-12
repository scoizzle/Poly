FROM node:23-alpine
WORKDIR /app
COPY sieve.js .
ENTRYPOINT ["node", "/app/sieve.js"]
