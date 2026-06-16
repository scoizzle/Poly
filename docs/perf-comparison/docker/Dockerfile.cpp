ARG SOURCE_FILE=sieve.cpp
FROM gcc:14-bookworm
ARG SOURCE_FILE
WORKDIR /src
COPY $SOURCE_FILE source.cpp
RUN g++ -O3 -march=native -std=c++17 -static-libstdc++ -o /app source.cpp
ENTRYPOINT ["/app"]
