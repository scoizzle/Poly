ARG SOURCE_FILE=sieve_numpy.py
FROM python:3-slim
ARG SOURCE_FILE
RUN pip install --no-cache-dir numpy
WORKDIR /app
COPY $SOURCE_FILE source.py
ENTRYPOINT ["python3", "/app/source.py"]
