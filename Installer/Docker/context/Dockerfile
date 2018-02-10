ARG ARCH=
FROM ${ARCH}mono:5-slim

RUN apt-get update && \
    apt-get install -y \
        libmono-sqlite4.0-cil \
        libmono-system-drawing4.0-cil \
        referenceassemblies-pcl && \
    rm -rf /var/lib/apt/lists

ENV TINI_VERSION v0.16.1
RUN apt-get update && \
    apt-get install -y curl && \
    curl -L -o /usr/sbin/tini https://github.com/krallin/tini/releases/download/${TINI_VERSION}/tini-$(dpkg --print-architecture) && \
    chmod 0755 /usr/sbin/tini && \
    apt-get autoremove --purge -y curl && \
    rm -rf /var/lib/apt/lists
ENTRYPOINT ["/usr/sbin/tini", "--"]

ENV XDG_CONFIG_HOME=/data
VOLUME /data

COPY context/duplicati context/duplicati-server /usr/bin/
RUN chmod 0755 /usr/bin/duplicati /usr/bin/duplicati-server

ARG CHANNEL=
ARG VERSION=
ENV DUPLICATI_CHANNEL=${CHANNEL}
ENV DUPLICATI_VERSION=${VERSION}
COPY duplicati /opt/duplicati

EXPOSE 8200
CMD ["/usr/bin/duplicati-server", "--webservice-port=8200", "--webservice-interface=any"]
