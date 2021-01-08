SCRIPTDIR=$( cd "$(dirname "$0")" ; pwd -P )

docker build $SCRIPTDIR/docker -t duplicati-selenium

export MSYS_NO_PATHCONV=1
docker run --rm -v $SCRIPTDIR/../../:/sources duplicati-selenium