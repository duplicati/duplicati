FROM selenium/standalone-firefox 

ENV DEBIAN_FRONTEND=noninteractive
RUN wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O ~/packages-microsoft-prod.deb
RUN sudo dpkg -i ~/packages-microsoft-prod.deb
RUN sudo apt-get update && sudo apt-get install -y python3-pip dotnet-sdk-6.0
RUN pip3 install selenium

ADD runner.sh /
CMD /runner.sh