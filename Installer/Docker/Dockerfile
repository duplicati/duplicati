FROM --platform=$TARGETPLATFORM alpine:latest

ENV TINI_VERSION v0.19.0
ADD https://github.com/krallin/tini/releases/download/${TINI_VERSION}/tini /tini
RUN chmod +x /tini
ENTRYPOINT ["/tini", "--"]

ENV XDG_CONFIG_HOME=/data
VOLUME /data

ARG CHANNEL=
ARG VERSION=
ENV DUPLICATI_CHANNEL=${CHANNEL}
ENV DUPLICATI_VERSION=${VERSION}

ARG TARGETARCH
COPY ./${TARGETARCH} /opt/duplicati

EXPOSE 8200
CMD ["/opt/duplicati/duplicati-server", "--webservice-port=8200", "--webservice-interface=any"]
