# encoding=utf-8
try:
    sql = u"UPDATE entities SET id= %s where id= %s"
    para = (1, 2)
    query = sql % para
    a = ""
except Exception as e:
    print e
