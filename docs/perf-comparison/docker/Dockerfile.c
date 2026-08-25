ARG SOURCE_FILE=sieve.c
FROM gcc:14-bookworm AS build
ARG SOURCE_FILE
WORKDIR /src
COPY $SOURCE_FILE source.c
RUN gcc -O3 -march=native -o /app source.c -lm

FROM debian:bookworm-slim
COPY --from=build /app /
ENTRYPOINT ["/app"]
