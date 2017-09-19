# -*- coding: UTF-8 -*-
import re

on_date_old_temp = "2015-02-03"
print re.search("^((?:19|20)\d\d)-(0[1-9]|1[012])-(0[1-9]|[12][0-9]|3[01])$", on_date_old_temp) != None
