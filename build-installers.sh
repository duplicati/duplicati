#!/bin/bash

if [ ! -f "$1" ]
then
  echo "Please supply the path to an existing zip binary as the first argument"
  exit 1
fi

GITHUB_TOKEN_FILE="${HOME}/.config/github-api-token"

FEDORA_INSTANCE_ID=i-deef5352
FEDORA_PUBKEY=AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBM3OWpUJOqoh9hq/k48g/FFLqnxUHxecVZM/jRD69Y/cn0OygsSyi3E5X/PVgtfyoced/HV788f9rDpLbY08jXg=

DEBIAN_INSTANCE_ID=i-f237887e
DEBIAN_PUBKEY=AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBJUDOPM7lBBOUtRbAZ8DUP7hHN88CvSvKzWQ15uXcOhdMgtqlznSQiLNfagD/AlqnPsOXWCnNN0fM+QAeMuMBEY=

WINDOWS_INSTANCE_ID=i-be348b32
WINDOWS_PUBKEY=AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBK5MD6Jg+p6rJmJfx8lWqIY+ZLDEYcGIwcf+eGzPSz4QKuOgxtDo0xgg3/OrxJibyk7sBWkT0f9RHD1qN9Nz/o8=

ZIPFILE=`basename "$1"`
VERSION=`echo "${ZIPFILE}" | cut -d "-" -f 2 | cut -d "_" -f 1`
BUILDTYPE=`echo "${ZIPFILE}" | cut -d "-" -f 2 | cut -d "_" -f 2`
BUILDTAG_RAW=`echo "${ZIPFILE}" | cut -d "." -f 1-4 | cut -d "-" -f 2-4`
BUILDTAG="${BUILDTAG_RAW//-}"

RPMNAME="duplicati-${VERSION}-${BUILDTAG}.noarch.rpm"
DEBNAME="duplicati_${VERSION}-1_all.deb"
MSI64NAME="Duplicati-${BUILDTAG_RAW}-x64.msi"
MSI32NAME="Duplicati-${BUILDTAG_RAW}-x86.msi"
DMGNAME="Duplicati-${BUILDTAG_RAW}.dmg"
PKGNAME="Duplicati-${BUILDTAG_RAW}.pkg"

UPDATE_TARGET="Updates/build/${BUILDTYPE}_target-${VERSION}"

KEYFILE=~/.ssh/duplicati-build-machines-key.pem
SSH_OPTIONS="-i ${KEYFILE} -o UserKnownHostsFile=./tmp/known_hosts"

echo "Filename: ${ZIPFILE}"
echo "Version: ${VERSION}"
echo "Buildtype: ${BUILDTYPE}"
echo "Buildtag: ${BUILDTAG}"
echo "RPMName: ${RPMNAME}"
echo "DEBName: ${DEBNAME}"

start_aws_instance() {

	local STATE=`aws ec2 describe-instances --profile=duplicati-builder --instance-ids $1 | python -c 'import sys, json; print json.load(sys.stdin)["Reservations"][0]["Instances"][0]["State"]["Name"]'`

	if [ "${STATE}" == "stopping" ]; then
		echo -n "Instance has state '${STATE}', waiting .."
		while [ "${STATE}" == "stopping" ]; do
			echo -n "."
			sleep 5
			local STATE=`aws ec2 describe-instances --profile=duplicati-builder --instance-ids $1 | python -c 'import sys, json; print json.load(sys.stdin)["Reservations"][0]["Instances"][0]["State"]["Name"]'`
		done
		echo ""
	fi

	local STATE=`aws ec2 start-instances --profile=duplicati-builder --instance-ids $1 | python -c 'import sys, json; print json.load(sys.stdin)["StartingInstances"][0]["CurrentState"]["Name"]'`

	if [ "${STATE}" != "running" ]; then
		echo -n "Instance has state '${STATE}', waiting .."

		while [ "${STATE}" != "running" ]; do		
			echo -n "."
			sleep 5
			local STATE=`aws ec2 describe-instances --profile=duplicati-builder --instance-ids $1 | python -c 'import sys, json; print json.load(sys.stdin)["Reservations"][0]["Instances"][0]["State"]["Name"]'`
		done

		echo ""
	fi

}

stop_aws_instance() {
	local STATE=`aws ec2 stop-instances --profile=duplicati-builder --instance-ids $1 | python -c 'import sys, json; print json.load(sys.stdin)["StoppingInstances"][0]["CurrentState"]["Name"]'`
	echo "Instance state is now: ${STATE}"
}

ssh_connect_to_instance() {
	local DNSNAME=`aws ec2 describe-instances --profile=duplicati-builder --instance-ids $1 | python -c 'import sys, json; print json.load(sys.stdin)["Reservations"][0]["Instances"][0]["PublicDnsName"]'`
	local IPADDR=`aws ec2 describe-instances --profile=duplicati-builder --instance-ids $1 | python -c 'import sys, json; print json.load(sys.stdin)["Reservations"][0]["Instances"][0]["PublicIpAddress"]'`

	if [ -d "./tmp" ]; then
		rm -rf "./tmp"
	fi

	mkdir "./tmp"

	echo "${DNSNAME},${IPADDR} ecdsa-sha2-nistp256 $3" > "./tmp/known_hosts"

	SSH_HOST="$2@${DNSNAME}"

	ssh ${SSH_OPTIONS} "${SSH_HOST}" "cd" > /dev/null
	local EXITCODE=$?

	if [ ! "${EXITCODE}" -eq 0 ]; then
		echo -n "Server did not allow login, waiting .."
		while [ ! "${EXITCODE}" -eq 0 ]; do		
			echo -n "."
			sleep 5

			ssh ${SSH_OPTIONS} "${SSH_HOST}" "cd" > /dev/null
			local EXITCODE=$?
		done
		echo ""
	fi	
}

ssh_upload_file() {
	scp ${SSH_OPTIONS} "$1" "${SSH_HOST}:"
}

ssh_run_commands() {
	cat "$1" | ssh ${SSH_OPTIONS} "${SSH_HOST}"
}

# Pre-boot instances to keep the waiting to a minimun

aws ec2 start-instances --profile=duplicati-builder --instance-ids "${DEBIAN_INSTANCE_ID}" &> /dev/null
aws ec2 start-instances --profile=duplicati-builder --instance-ids "${FEDORA_INSTANCE_ID}" &> /dev/null
aws ec2 start-instances --profile=duplicati-builder --instance-ids "${WINDOWS_INSTANCE_ID}" &> /dev/null

# Then do the local build to mask the waiting a little more

echo ""
echo ""
echo "Building OSX package locally ..."

cd Installer/OSX
bash "make-dmg.sh" "../../$1"
mv "Duplicati.dmg" "../../${UPDATE_TARGET}/${DMGNAME}"
mv "Duplicati.pkg" "../../${UPDATE_TARGET}/${PKGNAME}"
cd ../..


echo ""
echo ""
echo "Starting Debian build instance"

start_aws_instance "${DEBIAN_INSTANCE_ID}"

ssh_connect_to_instance "${DEBIAN_INSTANCE_ID}" "ubuntu" "${DEBIAN_PUBKEY}"

echo "Instance has started, uploading binary package ..."

ssh_upload_file "$1" 

echo "Running build script on server ..."

cat > "./tmp/debian-commands.sh" <<EOF
cd duplicati/Installer/debian
git pull
bash make-binary-package.sh "../../../${ZIPFILE}"
rm -rf "../../../${ZIPFILE}"
EOF

ssh_run_commands "./tmp/debian-commands.sh"

echo "Downloading binary package ..."

scp ${SSH_OPTIONS} "${SSH_HOST}:duplicati/Installer/debian/${DEBNAME}" "./tmp"
ssh ${SSH_OPTIONS} "${SSH_HOST}" "rm -rf duplicati/Installer/debian/duplicati_${VERSION}-*"

echo "Done, stopping instance ..."

stop_aws_instance "${DEBIAN_INSTANCE_ID}"

mv "./tmp/${DEBNAME}" "${UPDATE_TARGET}/"




echo ""
echo ""
echo "Starting Fedora build instance ..."

start_aws_instance "${FEDORA_INSTANCE_ID}"

ssh_connect_to_instance "${FEDORA_INSTANCE_ID}" "ec2-user" "${FEDORA_PUBKEY}"

echo "Instance has started, uploading binary package ..."

ssh_upload_file "$1" 

echo "Running build script on server ..."

cat > "./tmp/fedora-commands.sh" <<EOF
rm -rf ~/rpmbuild
rpmdev-setuptree
cd duplicati/Installer/fedora
git pull
bash make-binary-package.sh "../../../${ZIPFILE}"
rm -rf "../../../${ZIPFILE}"
EOF

ssh_run_commands "./tmp/fedora-commands.sh"

echo "Downloading binary package ..."

scp ${SSH_OPTIONS} "${SSH_HOST}:rpmbuild/RPMS/noarch/${RPMNAME}" "./tmp"

echo "Done, stopping instance ..."

stop_aws_instance "${FEDORA_INSTANCE_ID}"

mv "./tmp/${RPMNAME}" "${UPDATE_TARGET}/"


echo ""
echo ""
echo "Starting Windows build instance"

start_aws_instance "${WINDOWS_INSTANCE_ID}"

ssh_connect_to_instance "${WINDOWS_INSTANCE_ID}" "ec2-user" "${WINDOWS_PUBKEY}"

echo "Instance has started, uploading binary package ..."

ssh_upload_file "$1" 

echo "Running build script on server ..."

cat > "./tmp/windows-commands.sh" <<EOF
SET VS120COMNTOOLS=%VS140COMNTOOLS%
cd duplicati\\Installer\\Windows
git pull
build-msi.bat "..\\..\\..\\${ZIPFILE}"
del /q "..\\..\\..\\${ZIPFILE}"
EOF

ssh_run_commands "./tmp/windows-commands.sh"

echo "Downloading binary packages ..."

scp ${SSH_OPTIONS} "${SSH_HOST}:duplicati/Installer/Windows/Duplicati.msi" "./tmp"
scp ${SSH_OPTIONS} "${SSH_HOST}:duplicati/Installer/Windows/Duplicati-32bit.msi" "./tmp"

echo "Done, stopping instance ..."

stop_aws_instance "${WINDOWS_INSTANCE_ID}"

mv "./tmp/Duplicati.msi" "${UPDATE_TARGET}/${MSI64NAME}"
mv "./tmp/Duplicati-32bit.msi" "${UPDATE_TARGET}/${MSI32NAME}"

echo ""
echo ""
echo "Done building, uploading installers ..."

echo "{" > "./tmp/latest-installers.json"

process_installer() {
	aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/$1" "s3://updates.duplicati.com/${BUILDTYPE}/$1"

	local MD5=`md5 ${UPDATE_TARGET}/$1 | awk -F ' ' '{print $NF}'`
	local SHA1=`shasum -a 1 ${UPDATE_TARGET}/$1 | awk -F ' ' '{print $1}'`
	local SHA256=`shasum -a 256 ${UPDATE_TARGET}/$1 | awk -F ' ' '{print $1}'`

cat >> "./tmp/latest-installers.json" <<EOF
	"$2": {
		"name": "$1",
		"url": "http://updates.duplicati.com/${BUILDTYPE}/$1",
		"md5": "${MD5}",
		"sha1": "${SHA1}",
		"sha256": "${SHA256}"
	},
EOF

}

process_installer "${RPMNAME}" "rpm"
process_installer "${DEBNAME}" "deb"
process_installer "${DMGNAME}" "dmg"
process_installer "${PKGNAME}" "pkg"
process_installer "${MSI32NAME}" "msi86"
process_installer "${MSI64NAME}" "msi64"

cat >> "./tmp/latest-installers.json" <<EOF
	"done": true
}
EOF

aws --profile=duplicati-upload s3 cp "./tmp/latest-installers.json" "s3://updates.duplicati.com/${BUILDTYPE}/latest-installers.json"

GITHUB_TOKEN=`cat "${GITHUB_TOKEN_FILE}"`

if [ "x${GITHUB_TOKEN}" == "x" ]; then
	echo "No GITHUB_TOKEN found in environment, you can manually upload the binaries"
else
	for FILE in "${RPMNAME}" "${DEBNAME}" "${DMGNAME}" "${PKGNAME}" "${MSI32NAME}" "${MSI64NAME}"; do
		github-release upload \
		    --tag "v${VERSION}-${BUILDTAG_RAW}"  \
		    --name "${FILE}" \
		    --repo "duplicati" \
		    --user "duplicati" \
		    --security-token "${GITHUB_TOKEN}" \
		    --file "${UPDATE_TARGET}/${FILE}"
	done
fi

rm -rf "./tmp"
