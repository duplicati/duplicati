FROM ubuntu:latest

ENV DEBIAN_FRONTEND noninteractive

# Install common build tools
RUN set -uex; \
    apt-get update; \
    apt-get install --no-install-suggests --no-install-recommends -y \
      build-essential \
      debhelper \
      dpkg-dev \
    ; \
    apt-get clean all
RUN apt-get update && apt-get install -y curl
RUN curl -sL -o ~/packages-microsoft-prod.deb https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
RUN dpkg -i ~/packages-microsoft-prod.deb
RUN apt-get update && apt-get install -y dotnet-sdk-5.0

ADD debian /deb/debian
RUN chmod -x /deb/debian/duplicati.install
RUN chmod -x /deb/debian/docs

ADD runner.sh /
CMD /runner.sh