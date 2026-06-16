ARG SOURCE_FILE=sieve.js
FROM node:23-alpine
ARG SOURCE_FILE
WORKDIR /app
COPY $SOURCE_FILE source.js
ENTRYPOINT ["node", "/app/source.js"]
