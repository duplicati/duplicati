#!/bin/bash
SCRIPT_DIR="$( cd "$(dirname "$0")" ; pwd -P )"
. "${SCRIPT_DIR}/docker-run/error_handling.sh"
. "${SCRIPT_DIR}/.local_config.sh"

${ROOT_DIR}/pipeline/jobs/build_job.sh
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testcategories BulkNormal
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testcategories BulkNoSize
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testcategories SVNDataLong,SVNData,RecoveryTool
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testcategories Border
${ROOT_DIR}/pipeline/jobs/unittest_job.sh --testcategories Filter,Targeted,Purge,Serialization,WebApi,Utility,UriUtility,IO,ImportExport,Disruption,RestoreHandler,RepairHandler

