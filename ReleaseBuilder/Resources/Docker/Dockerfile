FROM --platform=$TARGETPLATFORM debian:latest

RUN apt update && apt install tini
ENTRYPOINT [ "/usr/bin/tini", "--" ]

RUN apt update -y && apt install -y libssl3 ca-certificates gnupg libicu72 

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
