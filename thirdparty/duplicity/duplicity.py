#!/usr/bin/env python
# duplicity -- Encrypted bandwidth efficient backup
# Version 0.4.10 released September 29, 2002
#
# Copyright (C) 2002 Ben Escoto <bescoto@stanford.edu>
#
# This file is part of duplicity.
#
# Duplicity is free software; you can redistribute it and/or modify it
# under the terms of the GNU General Public License as published by the
# Free Software Foundation; either version 3 of the License, or (at your
# option) any later version.
#
# Duplicity is distributed in the hope that it will be useful, but
# WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
# General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with duplicity; if not, write to the Free Software Foundation,
# Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
#
# See http://www.nongnu.org/duplicity for more information.
# Please send mail to me or the mailing list if you find bugs or have
# any suggestions.

from __future__ import generators
import getpass, gzip, os, sys, time, types
from duplicity import collections, commandline, diffdir, dup_temp, \
	 dup_time, file_naming, globals, gpg, log, manifest, patchdir, \
	 path, robust, tempdir

# If exit_val is not None, exit with given value at end.
exit_val = None


def get_passphrase():
	"""Get passphrase from environment or, failing that, from user"""
	try:
		return os.environ['PASSPHRASE']
	except KeyError:
		pass

	log.Log("PASSPHRASE variable not set, asking user.", 5)
	while 1:
		pass1 = getpass.getpass("GnuPG passphrase: ")
		pass2 = getpass.getpass("Retype to confirm: ")
		if not pass1 == pass2:
			print "First and second passphrases do not match!  Please try again."
			continue
		if not pass1 and not globals.gpg_profile.recipients:
			print "Cannot use empty passphrase with symmetric encryption!  Please try again."
			continue
		return pass1


def write_multivol(backup_type, tarblock_iter, backend):
	"""Encrypt volumes of tarblock_iter and write to backend

	backup_type should be "inc" or "full" and only matters here when
	picking the filenames.  The path_prefix will determine the names
	of the files written to backend.  Also writes manifest file.
	Returns number of bytes written.

	"""
	def get_indicies(tarblock_iter):
		"""Return start_index and end_index of previous volume"""
		start_index = tarblock_iter.recall_index()
		if start_index is None:
			start_index = ()
		end_index = tarblock_iter.get_previous_index()
		if end_index is None:
			end_index = start_index
		return start_index, end_index

	mf = manifest.Manifest().set_dirinfo()
	vol_num = 1; bytes_written = 0; at_end = 0
	while not at_end:
		# Create volume
		tarblock_iter.remember_next_index() # keep track of start index
		dest_filename = file_naming.get(backup_type, vol_num,
										encrypted = globals.encryption,
										gzipped = not globals.encryption)
		tdp = dup_temp.new_tempduppath(file_naming.parse(dest_filename))
		if globals.encryption:
			at_end = gpg.GPGWriteFile(tarblock_iter, tdp.name,
									  globals.gpg_profile,globals.volsize)
		else:
			at_end = gpg.GzipWriteFile(tarblock_iter, tdp.name,globals.volsize)
		tdp.setdata()

		# Add volume information to manifest
		vi = manifest.VolumeInfo()
		start_index, end_index = get_indicies(tarblock_iter)
		vi.set_info(vol_num, start_index, end_index)
		vi.set_hash("SHA1", gpg.get_hash("SHA1", tdp))
		mf.add_volume_info(vi)

		backend.put(tdp, dest_filename)
		vol_num += 1; bytes_written += tdp.getsize()
		tdp.delete()

	bytes_written += write_manifest(mf, backup_type, backend)
	return bytes_written


def write_manifest(mf, backup_type, backend):
	"""Write manifest to file in archive_dir and encrypted to backend

	Returns number of bytes written

	"""
	mf_string = mf.to_string()
	if globals.archive_dir:
		local_mf_name = file_naming.get(backup_type, manifest = 1)
		fin = dup_temp.get_fileobj_duppath(globals.archive_dir, local_mf_name)
		fin.write(mf_string)
		fin.close()

	sizelist = [] # will hold length of file in bytes
	remote_mf_name = file_naming.get(backup_type, manifest = 1,
									 encrypted = globals.encryption)
	remote_fin = backend.get_fileobj_write(remote_mf_name, sizelist = sizelist)
	remote_fin.write(mf_string)
	remote_fin.close()
	return sizelist[0]


def get_sig_fileobj(sig_type):
	"""Return a fileobj opened for writing, save results as signature

	If globals.archive_dir is available, save signatures there
	gzipped.  Save them on the backend encrypted as needed.

	"""
	assert sig_type == "full-sig" or sig_type == "new-sig"

	sig_filename = file_naming.get(sig_type, encrypted = globals.encryption,
								   gzipped = not globals.encryption)
	fh = globals.backend.get_fileobj_write(sig_filename)

	# by MDR.  This was changed to use addfilehandle so we get both
	# remote and local sig files
	if globals.archive_dir:
		local_sig_filename = file_naming.get(sig_type, gzipped = 1)
		fh.addfilehandle(dup_temp.get_fileobj_duppath(globals.archive_dir,
													  local_sig_filename))
	return fh


def full_backup(col_stats):
	"""Do full backup of directory to backend, using archive_dir"""
	sig_outfp = get_sig_fileobj("full-sig")
	tarblock_iter = diffdir.DirFull_WriteSig(globals.select, sig_outfp)
	bytes_written = write_multivol("full", tarblock_iter, globals.backend)
	sig_outfp.close()
	col_stats.set_values(sig_chain_warning = None).cleanup_signatures()
	print_statistics(diffdir.stats, bytes_written)
	

def check_sig_chain(col_stats):
	"""Get last signature chain for inc backup, or None if none available"""
	if not col_stats.matched_chain_pair:
		if globals.incremental:
			log.FatalError("Fatal Error: Unable to start incremental backup.  "
						   "Old signatures not found and incremental specified")
		else:
			log.Warn("No signatures found, switching to full backup.")
		return None
	return col_stats.matched_chain_pair[0]


def print_statistics(stats, bytes_written):
	"""If globals.print_statistics, print stats after adding bytes_written"""
	if globals.print_statistics:
		diffdir.stats.TotalDestinationSizeChange = bytes_written
		print diffdir.stats.get_stats_logstring("Backup Statistics")	


def incremental_backup(sig_chain):
	"""Do incremental backup of directory to backend, using archive_dir"""
	dup_time.setprevtime(sig_chain.end_time)
	new_sig_outfp = get_sig_fileobj("new-sig")
	tarblock_iter = diffdir.DirDelta_WriteSig(globals.select,
							  sig_chain.get_fileobjs(), new_sig_outfp)
	bytes_written = write_multivol("inc", tarblock_iter, globals.backend)
	new_sig_outfp.close()
	print_statistics(diffdir.stats, bytes_written)


def list_current(col_stats):
	"""List the files current in the archive (examining signature only)"""
	sig_chain = check_sig_chain(col_stats)
	if not sig_chain:
		log.FatalError("No signature data found, unable to list files.")
	path_iter = diffdir.get_combined_path_iter(sig_chain.get_fileobjs())
	for path in path_iter:
		if path.difftype != "deleted":
			print dup_time.timetopretty(path.getmtime()), \
				  path.get_relative_path()


def restore(col_stats):
	"""Restore archive in globals.backend to globals.local_path"""
	if not patchdir.Write_ROPaths(globals.local_path, 
								  restore_get_patched_rop_iter(col_stats)):
		if globals.restore_dir:
			log.FatalError("%s not found in archive, no files restored."
						   % (globals.restore_dir,))
		else:
			log.FatalError("No files found in archive - nothing restored.")


def restore_get_patched_rop_iter(col_stats):
	"""Return iterator of patched ROPaths of desired restore data"""
	if globals.restore_dir:
		index = tuple(globals.restore_dir.split("/"))
	else:
		index = ()
	time = globals.restore_time or dup_time.curtime
	backup_chain = col_stats.get_backup_chain_at_time(time)
	assert backup_chain, col_stats.all_backup_chains
	backup_setlist = backup_chain.get_sets_at_time(time)

	def get_fileobj_iter(backup_set):
		"""Get file object iterator from backup_set contain given index"""
		manifest = backup_set.get_manifest()
		for vol_num in manifest.get_containing_volumes(index):
			yield restore_get_enc_fileobj(backup_set.backend,
										  backup_set.volume_name_dict[vol_num],
										  manifest.volume_info_dict[vol_num])

	fileobj_iters = map(get_fileobj_iter, backup_setlist)
	tarfiles = map(patchdir.TarFile_FromFileobjs, fileobj_iters)
	return patchdir.tarfiles2rop_iter(tarfiles, index)


def restore_get_enc_fileobj(backend, filename, volume_info):
	"""Return plaintext fileobj from encrypted filename on backend

	If volume_info is set, the hash of the file will be checked,
	assuming some hash is available.  Also, if globals.sign_key is
	set, a fatal error will be raised if file not signed by sign_key.

	"""
	parseresults = file_naming.parse(filename)
	tdp = dup_temp.new_tempduppath(parseresults)
	backend.get(filename, tdp)
	restore_check_hash(volume_info, tdp)
	
	fileobj = tdp.filtered_open_with_delete("rb")
	if parseresults.encrypted and globals.gpg_profile.sign_key:
		restore_add_sig_check(fileobj)
	return fileobj


def restore_check_hash(volume_info, vol_path):
	"""Check the hash of vol_path path against data in volume_info"""
	hash_pair = volume_info.get_best_hash()
	if hash_pair:
		calculated_hash = gpg.get_hash(hash_pair[0], vol_path)
		if calculated_hash != hash_pair[1]:
			log.FatalError("Invalid data - %s hash mismatch:\n"
						   "Calculated hash: %s\n"
						   "Manifest hash: %s\n" %
						   (hash_pair[0], calculated_hash, hash_pair[1]))


def restore_add_sig_check(fileobj):
	"""Require signature when closing fileobj matches sig in gpg_profile"""
	assert (isinstance(fileobj, dup_temp.FileobjHooked) and
			isinstance(fileobj.fileobj, gpg.GPGFile)), fileobj
	def check_signature():
		"""Thunk run when closing volume file"""
		actual_sig = fileobj.fileobj.get_signature()
		if actual_sig != globals.gpg_profile.sign_key:
			log.FatalError("Volume was not signed by key %s, not %s" %
						   (actual_sig, globals.gpg_profile.sign_key))
	fileobj.addhook(check_signature)


def verify(col_stats):
	"""Verify files, logging differences"""
	global exit_val
	collated = diffdir.collate2iters(restore_get_patched_rop_iter(col_stats),
									 globals.select)
	diff_count = 0; total_count = 0
	for backup_ropath, current_path in collated:
		if not backup_ropath:
			backup_ropath = path.ROPath(current_path.index)
		if not current_path:
			current_path = path.ROPath(backup_ropath.index)
		if not backup_ropath.compare_verbose(current_path):
			diff_count += 1
		total_count += 1
	log.Log("Verify complete: %s %s compared, %s %s found." %
			(total_count, total_count == 1 and "file" or "files",
			 diff_count, diff_count == 1 and "difference" or "differences"), 3)
	if diff_count >= 1:
		exit_val = 1


def cleanup(col_stats):
	"""Delete the extraneous files in the current backend"""
	extraneous = col_stats.get_extraneous()
	if not extraneous:
		log.Warn("No extraneous files found, nothing deleted in cleanup.")
		return

	filestr = "\n".join(extraneous)
	if globals.force:
		if len(extraneous) > 1:
			log.Log("Deleting these files from backend:\n"+filestr, 3)
		else:
			log.Log("Deleting this file from backend:\n"+filestr, 3)
		col_stats.backend.delete(extraneous)
	else:
		if len(extraneous) > 1:
			log.Warn("Found the following files to delete:")
		else:
			log.Warn("Found the following file to delete:")
		log.Warn(filestr + "\nRun duplicity again with the --force "
				 "option to actually delete.")

def remove_all_but_n_full(col_stats):
	"Remove backup files older than the last n full backups."
	assert globals.keep_chains is not None

	globals.remove_time = col_stats.get_nth_last_full_backup_time(globals.keep_chains)

	return remove_old(col_stats)

def remove_old(col_stats):
	"""Remove backup files older than globals.remove_time from backend"""
	assert globals.remove_time is not None
	def set_times_str(setlist):
		"""Return string listing times of sets in setlist"""
		return "\n".join(map(lambda s: dup_time.timetopretty(s.get_time()),
							 setlist))

	req_list = col_stats.get_older_than_required(globals.remove_time)
	if req_list:
		log.Warn("There are backup set(s) at time(s):\n%s\nWhich can't be "
				 "deleted because newer sets depend on them." %
				 set_times_str(req_list))

	if (col_stats.matched_chain_pair and
		col_stats.matched_chain_pair[1].end_time < globals.remove_time):
		log.Warn("Current active backup chain is older than specified time.\n"
			 "However, it will not be deleted.  To remove all your backups,\n"
				 "manually purge the repository.")

	setlist = col_stats.get_older_than(globals.remove_time)
	if not setlist:
		log.Warn("No old backup sets found, nothing deleted.")
		return
	if globals.force:
		if len(setlist) > 1:
			log.Log("Deleting backup sets at times:\n" +
					set_times_str(setlist), 3)
		else:
			log.Log("Deleting backup set at times:\n" +
					set_times_str(setlist), 3)
		setlist.reverse() # save oldest for last
		for set in setlist:
			set.delete()
		col_stats.set_values(sig_chain_warning = None).cleanup_signatures()
	else:
		if len(setlist) > 1:
			log.Warn("Found old backup sets at the following times:")
		else:
			log.Warn("Found old backup set at the following time:")
		log.Warn(set_times_str(setlist) + 
				 "\nRerun command with --force option to actually delete.")


def check_last_manifest(col_stats):
	"""Check consistency and hostname/directory of last manifest"""
	if not col_stats.all_backup_chains:
		return
	last_backup_set = col_stats.all_backup_chains[-1].get_last()
	last_backup_set.check_manifests()


def main():
	"""Start/end here"""
	dup_time.setcurtime()
	action = commandline.ProcessCommandLine(sys.argv[1:])
	col_stats = collections.CollectionsStatus(globals.backend,
											  globals.archive_dir).set_values()
	log.Log("Collection Status\n-----------------\n" + str(col_stats), 8)

	last_full_time = col_stats.get_last_full_backup_time()
	if last_full_time > 0:
		log.Log("Last full backup date: " + dup_time.timetopretty(last_full_time), 4)
	else:
		log.Log("Last full backup date: none", 4)
	if action == "inc" and last_full_time < globals.full_force_time:
		log.Log("Last full backup is too old, forcing full backup", 3)
		action = "full"

	os.umask(077)
	
	# for public key encryption (without signing!), no passphrase is required.
	pubkey_only = (not globals.gpg_profile.sign_key and
				   globals.gpg_profile.recipients and
				   globals.encryption)

	# cases where we do not need to get a passphrase:
	# full: with pubkey enc. doesn't depend on old encrypted info
	# inc and pubkey enc.: need a manifest, which the archive dir has unencrypted
	# with encryption disabled
	# listing files: needs a manifest, but the archive dir has that
	# collection status only looks at a repository
	if ((action == "full" and pubkey_only) or
		(action == "inc" and pubkey_only and globals.archive_dir) or
		(not globals.encryption) or
		(action == "list-current" and globals.archive_dir) or
		action in ["collection-status",
				   "remove-older-then",
				   "remove-all-but-n-full",
				   ]):
		globals.gpg_profile.passphrase = ""
	else:
		globals.gpg_profile.passphrase = get_passphrase()

	if action == "restore":
		restore(col_stats)
	elif action == "verify":
		verify(col_stats)
	elif action == "list-current":
		list_current(col_stats)
	elif action == "collection-status":
		print str(col_stats)
	elif action == "cleanup":
		cleanup(col_stats)
	elif action == "remove-old":
		remove_old(col_stats)
	elif action == "remove-all-but-n-full":
		remove_all_but_n_full(col_stats)
	else:
		assert action == "inc" or action == "full", action
		if action == "full":
			full_backup(col_stats)
		else:
			check_last_manifest(col_stats) # not needed for full backup
			sig_chain = check_sig_chain(col_stats)
			if not sig_chain:
				full_backup(col_stats)
			else:
				incremental_backup(sig_chain)
	globals.backend.close()
	if exit_val is not None:
		sys.exit(exit_val)

def with_tempdir(fn):
	globals.badfiles = []
	try:
		fn()
	finally:
		for i in globals.badfiles:
			try:
		  		os.unlink(i)
			except:
				pass
		  	
		tempdir.default().cleanup()

if __name__ == "__main__":
	with_tempdir(main)
