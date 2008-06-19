# odbc test suite kindly contributed by Frank Millman.
import sys
import os
import unittest
import odbc
import dbi
import tempfile

# We use the DAO ODBC driver
from win32com.client.gencache import EnsureDispatch
from win32com.client import constants

class TestStuff(unittest.TestCase):
    def setUp(self):
        self.db_filename = None
        self.conn = self.cur = None
        try:
            # Test any database if a connection string is supplied...
            conn_str = os.environ['TEST_ODBC_CONNECTION_STRING']
        except KeyError:
            # Create a local MSAccess DB for testing.
            self.db_filename = os.path.join(tempfile.gettempdir(), "test_odbc.mdb")
            if os.path.isfile(self.db_filename):
                os.unlink(self.db_filename)
    
            # Create a brand-new database - what is the story with these?
            for suffix in (".36", ".35", ".30"):
                try:
                    dbe = EnsureDispatch("DAO.DBEngine" + suffix)
                    break
                except pythoncom.com_error:
                    pass
            else:
                raise RuntimeError, "Can't find a DB engine"
    
            workspace = dbe.Workspaces(0)
    
            newdb = workspace.CreateDatabase(self.db_filename, 
                                             constants.dbLangGeneral,
                                             constants.dbEncrypt)
    
            newdb.Close()
    
            conn_str = "Driver={Microsoft Access Driver (*.mdb)};dbq=%s;Uid=;Pwd=;" \
                       % (self.db_filename,)
        self.conn = odbc.odbc(conn_str)
        # And we expect a 'users' table for these tests.
        self.cur = self.conn.cursor()
        try:
            self.cur.execute("""drop table pywin32test_users""")
        except (odbc.error, dbi.progError):
            pass
        
        self.assertEqual(self.cur.execute(
            """create table pywin32test_users (
                    userid varchar(5),  username varchar(25),
                    bitfield bit,       intfield integer,
                    floatfield float
                )"""),-1)

    def tearDown(self):
        if self.cur is not None:
            try:
                self.cur.execute("""drop table pywin32test_users""")
            except (odbc.error, dbi.progError), why:
                print "Failed to delete test table:", why

            self.cur.close()
            self.cur = None
        if self.conn is not None:
            self.conn.close()
            self.conn = None
        if self.db_filename is not None:
            os.unlink(self.db_filename)

    def test_insert_select(self, userid='Frank', username='Frank Millman'):
        self.assertEqual(self.cur.execute("insert into pywin32test_users (userid, username) \
            values (?,?)", [userid, username]),1)
        self.assertEqual(self.cur.execute("select * from pywin32test_users \
            where userid = ?", [userid.lower()]),0)
        self.assertEqual(self.cur.execute("select * from pywin32test_users \
            where username = ?", [username.lower()]),0)

    def test_insert_select_large(self):
        # hard-coded 256 limit in ODBC to trigger large value support
        # (but for now ignore a warning about the value being truncated)
        try:
            self.test_insert_select(userid='Frank' * 200, username='Frank Millman' * 200)
        except dbi.noError:
            pass

    def test_insert_select_unicode(self, userid=u'Frank', username=u"Frank Millman"):
        self.assertEqual(self.cur.execute("insert into pywin32test_users (userid, username)\
            values (?,?)", [userid, username]),1)
        self.assertEqual(self.cur.execute("select * from pywin32test_users \
            where userid = ?", [userid.lower()]),0)
        self.assertEqual(self.cur.execute("select * from pywin32test_users \
            where username = ?", [username.lower()]),0)

    def test_insert_select_unicode_ext(self):
        userid = unicode("t-\xe0\xf2", "mbcs")
        username = unicode("test-\xe0\xf2 name", "mbcs")
        self.test_insert_select_unicode(userid, username)

    def _test_val(self, fieldName, value):
        self.cur.execute("delete from pywin32test_users where userid='Frank'")
        self.assertEqual(self.cur.execute(
            "insert into pywin32test_users (userid, %s) values (?,?)" % fieldName,
            ["Frank", value]), 1)
        self.cur.execute("select %s from pywin32test_users where userid = ?" % fieldName,
                         ["Frank"])
        rows = self.cur.fetchmany()
        self.failUnlessEqual(1, len(rows))
        row = rows[0]
        self.failUnlessEqual(row[0], value)

    def testBit(self):
        self._test_val('bitfield', 1)
        self._test_val('bitfield', 0)

    def testInt(self):
        self._test_val('intfield', 1)
        self._test_val('intfield', 0)
        self._test_val('intfield', sys.maxint)

    def testFloat(self):
        self._test_val('floatfield', 1.01)
        self._test_val('floatfield', 0)

    def testVarchar(self, ):
        self._test_val('username', 'foo')

    def test_set_nonzero_length(self):
        self.assertEqual(self.cur.execute("insert into pywin32test_users (userid,username) "
            "values (?,?)",['Frank', 'Frank Millman']),1)
        self.assertEqual(self.cur.execute("update pywin32test_users set username = ?",
            ['Frank']),1)
        self.assertEqual(self.cur.execute("select * from pywin32test_users"),0)
        self.assertEqual(len(self.cur.fetchone()[1]),5)

    def test_set_zero_length(self):
        self.assertEqual(self.cur.execute("insert into pywin32test_users (userid,username) "
            "values (?,?)",['Frank', '']),1)
        self.assertEqual(self.cur.execute("select * from pywin32test_users"),0)
        self.assertEqual(len(self.cur.fetchone()[1]),0)

    def test_set_zero_length_unicode(self):
        self.assertEqual(self.cur.execute("insert into pywin32test_users (userid,username) "
            "values (?,?)",[u'Frank', u'']),1)
        self.assertEqual(self.cur.execute("select * from pywin32test_users"),0)
        self.assertEqual(len(self.cur.fetchone()[1]),0)

if __name__ == '__main__':
    unittest.main()
