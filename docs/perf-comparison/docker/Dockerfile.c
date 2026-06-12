FROM gcc:14-bookworm AS build
WORKDIR /src
COPY sieve.c .
RUN gcc -O3 -march=native -o /sieve sieve.c -lm

FROM debian:bookworm-slim
COPY --from=build /sieve /
ENTRYPOINT ["/sieve"]
