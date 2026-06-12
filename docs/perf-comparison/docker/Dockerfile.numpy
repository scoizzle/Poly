FROM python:3-slim
RUN pip install --no-cache-dir numpy
WORKDIR /app
COPY sieve_numpy.py ./sieve.py
ENTRYPOINT ["python3", "/app/sieve.py"]
