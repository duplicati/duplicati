FROM --platform=$TARGETPLATFORM debian:latest

RUN apt update && apt install tini
ENTRYPOINT [ "/usr/bin/tini", "--" ]

ENV XDG_CONFIG_HOME=/data
VOLUME /data

ARG CHANNEL=
ARG VERSION=
ENV DUPLICATI_CHANNEL=${CHANNEL}
ENV DUPLICATI_VERSION=${VERSION}

ARG TARGETARCH
COPY ./${TARGETARCH} /opt/duplicati

EXPOSE 8200
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
CMD ["/opt/duplicati/duplicati-server", "--webservice-port=8200", "--webservice-interface=any"]
