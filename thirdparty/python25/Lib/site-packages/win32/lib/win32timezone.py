# -*- coding: UTF-8 -*-

"""
win32timezone:
	Module for handling datetime.tzinfo time zones using the windows
registry for time zone information.  The time zone names are dependent
on the registry entries defined by the operating system.

	Currently, this module only supports the Windows NT line of products
and not Windows 95/98/Me.

	This module may be tested using the doctest module.

	Written by Jason R. Coombs (jaraco@jaraco.com).
	Copyright © 2003.
	All Rights Reserved.	

	To use this time zone module with the datetime module, simply pass
the TimeZoneInfo object to the datetime constructor.  For example,

>>> import win32timezone, datetime
>>> assert 'Mountain Standard Time' in win32timezone.GetTimeZoneNames()
>>> tzi = TimeZoneInfo( 'Mountain Standard Time' )
>>> now = datetime.datetime.now( tzi )

	The now object is now a time-zone aware object, and daylight savings-
aware methods may be called on it.

>>> now.utcoffset() in ( datetime.timedelta(-1, 61200), datetime.timedelta(-1, 64800) )
True

(note that the result of utcoffset call will be different based on when now was
generated, unless standard time is always used)

>>> now = datetime.datetime.now( TimeZoneInfo( 'Mountain Standard Time', True ) )
>>> now.utcoffset()
datetime.timedelta(-1, 61200)

>>> aug2 = datetime.datetime( 2003, 8, 2, tzinfo = tzi )
>>> aug2.utctimetuple()
(2003, 8, 2, 6, 0, 0, 5, 214, 0)
>>> nov2 = datetime.datetime( 2003, 11, 2, tzinfo = tzi )
>>> nov2.utctimetuple()
(2003, 11, 2, 7, 0, 0, 6, 306, 0)

To convert from one timezone to another, just use the astimezone method.

>>> aug2.isoformat()
'2003-08-02T00:00:00-06:00'
>>> aug2est = aug2.astimezone( win32timezone.TimeZoneInfo( 'Eastern Standard Time' ) )
>>> aug2est.isoformat()
'2003-08-02T02:00:00-04:00'

calling the displayName member will return the display name as set in the
registry.

>>> est = win32timezone.TimeZoneInfo( 'Eastern Standard Time' )
>>> est.displayName
u'(GMT-05:00) Eastern Time (US & Canada)'

>>> gmt = win32timezone.TimeZoneInfo( 'GMT Standard Time', True )
>>> gmt.displayName
u'(GMT) Greenwich Mean Time : Dublin, Edinburgh, Lisbon, London'

TimeZoneInfo now supports being pickled and comparison
>>> import pickle
>>> tz = win32timezone.TimeZoneInfo( 'China Standard Time' )
>>> tz == pickle.loads( pickle.dumps( tz ) )
True
"""
from __future__ import generators

__author__ = 'Jason R. Coombs <jaraco@jaraco.com>'
__version__ = '$Revision: 1.5 $'[11:-2]
__vssauthor__ = '$Author: mhammond $'[9:-2]
__date__ = '$Modtime: 04-04-14 10:52 $'[10:-2]

import os, _winreg, struct, datetime

class TimeZoneInfo( datetime.tzinfo ):
	"""
	Main class for handling win32 time zones.
	Usage:
		TimeZoneInfo( <Time Zone Standard Name>, [<Fix Standard Time>] )
	If <Fix Standard Time> evaluates to True, daylight savings time is calculated in the same
		way as standard time.
	"""

	# this key works for WinNT+, but not for the Win95 line.
	tzRegKey = r'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones'
		
	def __init__( self, timeZoneName, fixedStandardTime=False ):
		self.timeZoneName = timeZoneName
		key = self._FindTimeZoneKey()
		self._LoadInfoFromKey( key )
		self.fixedStandardTime = fixedStandardTime

	def _FindTimeZoneKey( self ):
		"""Find the registry key for the time zone name (self.timeZoneName)."""
		# for multi-language compatability, match the time zone name in the
		# "Std" key of the time zone key.
		zoneNames = dict( GetIndexedTimeZoneNames( 'Std' ) )
		# Also match the time zone key name itself, to be compatible with
		# English-based hard-coded time zones.
		timeZoneName = zoneNames.get( self.timeZoneName, self.timeZoneName )
		tzRegKeyPath = os.path.join( self.tzRegKey, timeZoneName )
		try:
			key = _winreg.OpenKeyEx( _winreg.HKEY_LOCAL_MACHINE, tzRegKeyPath )
		except:
			raise ValueError, 'Timezone Name %s not found.' % timeZoneName
		return key

	def __getinitargs__( self ):
		return ( self.timeZoneName, )

	def _LoadInfoFromKey( self, key ):
		"""Loads the information from an opened time zone registry key
		into relevant fields of this TZI object"""
		self.displayName = _winreg.QueryValueEx( key, "Display" )[0]
		self.standardName = _winreg.QueryValueEx( key, "Std" )[0]
		self.daylightName = _winreg.QueryValueEx( key, "Dlt" )[0]
		# TZI contains a structure of time zone information and is similar to
		#  TIME_ZONE_INFORMATION described in the Windows Platform SDK
		winTZI, type = _winreg.QueryValueEx( key, "TZI" )
		winTZI = struct.unpack( '3l8h8h', winTZI )
		makeMinuteTimeDelta = lambda x: datetime.timedelta( minutes = x )
		self.bias, self.standardBiasOffset, self.daylightBiasOffset = \
				   map( makeMinuteTimeDelta, winTZI[:3] )
		# daylightEnd and daylightStart are 8-tuples representing a Win32 SYSTEMTIME structure
		self.daylightEnd, self.daylightStart = winTZI[3:11], winTZI[11:19]

	def __repr__( self ):
		result = '%s( %s' % ( self.__class__.__name__, repr( self.timeZoneName ) )
		if self.fixedStandardTime:
			result += ', True'
		result += ' )'
		return result

	def __str__( self ):
		return self.displayName

	def tzname( self, dt ):
		if self.dst( dt ) == self.daylightBiasOffset:
			result = self.daylightName
		elif self.dst( dt ) == self.standardBiasOffset:
			result = self.standardName
		return result
		
	def _getStandardBias( self ):
		return self.bias + self.standardBiasOffset
	standardBias = property( _getStandardBias )

	def _getDaylightBias( self ):
		return self.bias + self.daylightBiasOffset
	daylightBias = property( _getDaylightBias )

	def utcoffset( self, dt ):
		"Calculates the utcoffset according to the datetime.tzinfo spec"
		if dt is None: return
		return -( self.bias + self.dst( dt ) )

	def dst( self, dt ):
		"Calculates the daylight savings offset according to the datetime.tzinfo spec"
		if dt is None: return
		assert dt.tzinfo is self
		result = self.standardBiasOffset

		try:
			dstStart = self.GetDSTStartTime( dt.year )
			dstEnd = self.GetDSTEndTime( dt.year )

			if dstStart <= dt.replace( tzinfo=None ) < dstEnd and not self.fixedStandardTime:
				result = self.daylightBiasOffset
		except ValueError:
			# there was an error parsing the time zone, which is normal when a
			#  start and end time are not specified.
			pass

		return result

	def GetDSTStartTime( self, year ):
		"Given a year, determines the time when daylight savings time starts"
		return self._LocateDay( year, self.daylightStart )

	def GetDSTEndTime( self, year ):
		"Given a year, determines the time when daylight savings ends."
		return self._LocateDay( year, self.daylightEnd )
	
	def _LocateDay( self, year, win32SystemTime ):
		"""
		Takes a SYSTEMTIME structure as retrieved from a TIME_ZONE_INFORMATION
		structure and interprets it based on the given year to identify the actual day.

		This method is necessary because the SYSTEMTIME structure refers to a day by its
		day of the week or week of the month (e.g. 4th saturday in April).

		Refer to the Windows Platform SDK for more information on the SYSTEMTIME
		and TIME_ZONE_INFORMATION structures.
		"""
		month = win32SystemTime[ 1 ]
		# MS stores Sunday as 0, Python datetime stores Monday as zero
		targetWeekday = ( win32SystemTime[ 2 ] + 6 ) % 7
		# win32SystemTime[3] is the week of the month, so the following
		#  is the first day of that week
		day = ( win32SystemTime[ 3 ] - 1 ) * 7 + 1
		hour, min, sec, msec = win32SystemTime[4:]
		result = datetime.datetime( year, month, day, hour, min, sec, msec )
		# now the result is the correct week, but not necessarily the correct day of the week
		daysToGo = targetWeekday - result.weekday()
		result += datetime.timedelta( daysToGo )
		# if we selected a day in the month following the target month,
		#  move back a week or two.
		# This is necessary because Microsoft defines the fifth week in a month
		#  to be the last week in a month and adding the time delta might have
		#  pushed the result into the next month.
		while result.month == month + 1:
			result -= datetime.timedelta( weeks = 1 )
		return result

	def __cmp__( self, other ):
		return cmp( self.__dict__, other.__dict__ )

def _RegKeyEnumerator( key ):
	return _RegEnumerator( key, _winreg.EnumKey )

def _RegValueEnumerator( key ):
	return _RegEnumerator( key, _winreg.EnumValue )

def _RegEnumerator( key, func ):
	"Enumerates an open registry key as an iterable generator"
	index = 0
	try:
		while 1:
			yield func( key, index )
			index += 1
	except WindowsError: pass
	
def _RegKeyDict( key ):
	values = _RegValueEnumerator( key )
	values = tuple( values )
	return dict( map( lambda (name,value,type): (name,value), values ) )

def GetTimeZoneNames( ):
	"Returns the names of the time zones as defined in the registry"
	key = _winreg.OpenKeyEx( _winreg.HKEY_LOCAL_MACHINE, TimeZoneInfo.tzRegKey )
	return _RegKeyEnumerator( key )

def GetIndexedTimeZoneNames( index_key = 'Index' ):
	"""Returns the names of the time zones as defined in the registry, but
	includes an index by which they may be sorted.  Default index is "Index"
	by which they may be sorted longitudinally."""
	for timeZoneName in GetTimeZoneNames():
		tzRegKeyPath = os.path.join( TimeZoneInfo.tzRegKey, timeZoneName )
		key = _winreg.OpenKeyEx( _winreg.HKEY_LOCAL_MACHINE, tzRegKeyPath )
		tzIndex, type = _winreg.QueryValueEx( key, index_key )
		yield ( tzIndex, timeZoneName )

def GetSortedTimeZoneNames( ):
	""" Uses GetIndexedTimeZoneNames to return the time zone names sorted
	longitudinally."""
	tzs = list( GetIndexedTimeZoneNames() )
	tzs.sort()
	return zip( *tzs )[1]

def GetLocalTimeZone( ):
	"""Returns the local time zone as defined by the operating system in the
	registry.
	Note that this will only work if the TimeZone in the registry has not been
	customized.  It should have been selected from the Windows interface.
	>>> localTZ = GetLocalTimeZone()
	>>> nowLoc = datetime.datetime.now( localTZ )
	>>> nowUTC = datetime.datetime.utcnow( )
	>>> ( nowUTC - nowLoc ) < datetime.timedelta( seconds = 5 )
	Traceback (most recent call last):
	  ...
	TypeError: can't subtract offset-naive and offset-aware datetimes

	>>> nowUTC = nowUTC.replace( tzinfo = TimeZoneInfo( 'GMT Standard Time', True ) )

	Now one can compare the results of the two offset aware values	
	>>> ( nowUTC - nowLoc ) < datetime.timedelta( seconds = 5 )
	True
	"""
	tzRegKey = r'SYSTEM\CurrentControlSet\Control\TimeZoneInformation'
	key = _winreg.OpenKeyEx( _winreg.HKEY_LOCAL_MACHINE, tzRegKey )
	local = _RegKeyDict( key )
	# if the user has not checked "Automatically adjust clock for daylight
	# saving changes" in the Date and Time Properties control, the standard
	# and daylight values will be the same.  If this is the case, create a
	# timezone object fixed to standard time.
	fixStandardTime = local['StandardName'] == local['DaylightName'] and \
					local['StandardBias'] == local['DaylightBias']
	return TimeZoneInfo( local['StandardName'], fixStandardTime )
