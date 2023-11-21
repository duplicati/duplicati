# Installation instructions when building locally (March 2023)
# add ppa https://download.mono-project.com/repo/ubuntu stable-focal main
# install p7zip, build-essential, debhelper, dpkg-dev, mono-devel,
# libappindicator0.1-cil-dev, ca-certificates-mono, gtk-sharp2

RELEASE_TIMESTAMP=$(date +%Y-%m-%d)

RELEASE_INC_VERSION=$(cat Updates/build_version.txt)
RELEASE_INC_VERSION=$((RELEASE_INC_VERSION+1))

RELEASE_TYPE="canary"

RELEASE_VERSION="2.0.7.${RELEASE_INC_VERSION}"
RELEASE_NAME="${RELEASE_VERSION}_${RELEASE_TYPE}_${RELEASE_TIMESTAMP}"

RELEASE_FILE_NAME="duplicati-${RELEASE_NAME}"

export RUNTMP=$HOME
bash Installer/bundleduplicati.sh $RELEASE_FILE_NAME
mkdir -p $RUNTMP/artifacts
# cp $RUNTMP/$RELEASE_FILE_NAME $RUNTMP/artifacts/$RELEASE_FILE_NAME.zip
pushd $RUNTMP
    mv *_build.zip artifacts
popd
# Build MAC OS INSTALLERS
# cd Installer/OSX
# bash make-dmg.sh $RUNTMP/$RELEASE_FILE_NAME
# mv *.dmg $RUNTMP/artifacts
# mv *.pkg $RUNTMP/artifacts
# cd ../..