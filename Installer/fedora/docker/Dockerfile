FROM fedora:36

# Install common build tools
RUN dnf -y install deltarpm
RUN dnf -y upgrade

#No Longer needed
#RUN rpm --import https://packages.microsoft.com/keys/microsoft.asc
#RUN curl --output /etc/yum.repos.d/microsoft-prod.repo https://packages.microsoft.com/config/fedora/33/prod.repo

RUN dnf check-update
RUN dnf -y --allowerasing install @"Minimal Install" @buildsys-build yum-utils rpm-sign gnupg rpmdevtools desktop-file-utils dos2unix dotnet-sdk-6.0

ADD buildroot /buildroot

ADD runner.sh /
CMD /runner.sh
