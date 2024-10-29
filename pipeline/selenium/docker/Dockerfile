FROM selenium/standalone-chrome

SHELL ["/bin/bash", "-c"]
ENV DEBIAN_FRONTEND=noninteractive
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV AUTOUPDATER_Duplicati_SKIP_UPDATE=1
RUN source /etc/os-release && wget https://packages.microsoft.com/config/$ID/$VERSION_ID/packages-microsoft-prod.deb -O ~/packages-microsoft-prod.deb
RUN sudo dpkg -i ~/packages-microsoft-prod.deb
RUN sudo apt update && sudo apt install -y python3-pip dotnet-sdk-8.0
RUN sudo pip3 install selenium --break-system-packages
RUN sudo pip3 install --upgrade urllib3 --break-system-packages
RUN sudo pip3 install chromedriver-autoinstaller --break-system-packages

ADD runner.sh /
CMD /runner.sh