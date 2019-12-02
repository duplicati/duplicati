#!/bin/bash
. "$( cd "$(dirname "$0")" ; pwd -P )/error_handling.sh"
. "$( cd "$(dirname "$0")" ; pwd -P )/markers.sh"

function sync_dirs () {
  rsync_delete_option="--delete"
  for (( i=1; i<$SOURCE_DIR_NUM+1; i++ )); do
    travis_mark_begin "SYNCING DIR source_${i}"
    rsync -a $rsync_delete_option "/source_${i}/" "/application/"
    unset rsync_delete_option
    travis_mark_end "SYNCING DIR source_${i}"
  done
}

function setup () {
   if [ -f /sbin/apk ]; then
      apk --update add $DOCKER_PACKAGES
      return
   fi

   if [ -f /usr/bin/apt-get ]; then
      export DEBIAN_FRONTEND=noninteractive
      apt-get update && apt-get install -y $DOCKER_PACKAGES
      return
   fi
}

function clean_up () {
  if [[ -z $KEEP_TARGET_FILTER ]]; then
    return
  fi

  cd /application
  find . -type f ! -regex "${KEEP_TARGET_FILTER}" -delete
  find . -type d -empty -delete

  for (( i=1; i<${#KEEP_SOURCE_FILTER[@]}+1; i++ )); do
    cd /source_${i}
    find . -type f ! -regex "${KEEP_SOURCE_FILTER[$i-1]}" -delete
    find . -type d -empty -delete
  done
}

function parse_options () {
  KEEP_SOURCE_FILTER=()

  while true ; do
      case "$1" in
        --command)
          DOCKER_COMMAND="$2"
          ;;
        --packages)
          DOCKER_PACKAGES="$2"
          ;;
        --sourcedirnum)
          SOURCE_DIR_NUM="$2"
          ;;
        --keeptargetfilter)
          KEEP_TARGET_FILTER="$2"
          ;;
        --keepsourcefilter)
          KEEP_SOURCE_FILTER[${#KEEP_SOURCE_FILTER[@]}]="$2"
          ;;
        --*)
          if [[ $2 =~ ^--.* || -z $2 ]]; then
            FORWARD_OPTS[${#FORWARD_OPTS[@]}]="$1"
          else
            FORWARD_OPTS[${#FORWARD_OPTS[@]}]="$1"
            FORWARD_OPTS[${#FORWARD_OPTS[@]}]="$2"
          fi
          ;;
      esac

      if [[ -z $1 ]]; then
        break
      elif [[ $2 =~ ^--.* || -z $2 ]]; then
        shift
      else
        shift
        shift
      fi
  done
}

function set_user_script_vars () {
  option_found=false
  for arg in ${FORWARD_OPTS[@]}; do
    if [[ $option_found == true ]]; then
      eval $option=$arg
      export $option
      option_found=false
    elif [[ $arg =~ ^--.* ]]; then
      option_found=true
      option=${arg#"--"}
    elif [[ $arg =~ ^-.* ]]; then
      flag=${arg#"-"}
      eval $flag=true
      export $flag
    fi
  done
}

parse_options "$@"
setup
sync_dirs
cd /application
set_user_script_vars
$DOCKER_COMMAND "${FORWARD_OPTS[@]}"
clean_up