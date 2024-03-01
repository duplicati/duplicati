ARG PARENT_IMAGE
FROM --platform=$TARGETPLATFORM ${PARENT_IMAGE}

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        curl \
        mono-complete \
        libmono-sqlite4.0-cil \
        libmono-system-drawing4.0-cil \
        libmono-system-net-http-webrequest4.0-cil \
        libmono-system-web4.0-cil \
        referenceassemblies-pcl && \
    rm -rf /var/lib/apt/lists && \
    cert-sync /etc/ssl/certs/ca-certificates.crt && \
    # this obsolete cert can mess the certificate chain because of a Mono bug
    rm -f /usr/share/ca-certificates/mozilla/DST_Root_CA_X3.crt && \
    update-ca-certificates

ENV TINI_VERSION v0.16.1
RUN curl -L -o /usr/sbin/tini https://github.com/krallin/tini/releases/download/${TINI_VERSION}/tini-$(dpkg --print-architecture) && \
    chmod 0755 /usr/sbin/tini
ENTRYPOINT ["/usr/sbin/tini", "--"]

ENV XDG_CONFIG_HOME=/data
VOLUME /data

COPY context/duplicati-cli context/duplicati-server /usr/bin/
RUN chmod 0755 /usr/bin/duplicati-cli /usr/bin/duplicati-server

ARG CHANNEL=
ARG VERSION=
ENV DUPLICATI_CHANNEL=${CHANNEL}
ENV DUPLICATI_VERSION=${VERSION}
COPY duplicati /opt/duplicati

EXPOSE 8200
CMD ["/usr/bin/duplicati-server", "--webservice-port=8200", "--webservice-interface=any"]
