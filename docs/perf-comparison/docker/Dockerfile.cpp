FROM gcc:14-bookworm
WORKDIR /src
COPY sieve.cpp .
RUN g++ -O3 -march=native -std=c++17 -static-libstdc++ -o /sieve sieve.cpp
ENTRYPOINT ["/sieve"]
