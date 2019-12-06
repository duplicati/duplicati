#!/bin/bash
SCRIPT_DIR="$( cd "$(dirname "$0")" ; pwd -P )"
. "${SCRIPT_DIR}/docker-run/error_handling.sh"
. "${SCRIPT_DIR}/.local_config.sh"

${ROOT_DIR}/pipeline/jobs/build_job.sh
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testcategories BulkNormal --testdata data.zip
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testcategories BulkNoSize --testdata data.zip
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testcategories SVNDataLong,SVNData --testdata DSMCBE.zip
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testcategories Border
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testctegories Filter,Targeted,Purge,Serialization,WebApi,Utility,UriUtility,IO,ImportExport,Disruption

