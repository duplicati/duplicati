FROM ubuntu

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y curl zip
RUN curl -sL -o ~/packages-microsoft-prod.deb https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
RUN dpkg -i ~/packages-microsoft-prod.deb
RUN apt-get update && apt-get install -y dotnet-sdk-6.0

ADD runner.sh /
CMD /runner.sh