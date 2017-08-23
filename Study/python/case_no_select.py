# encoding=utf-8
import jieba
import xlrd

case_str = ""
data = xlrd.open_workbook(u"E:/资料文档/开庭公告案由清洗/案由筛选.xlsx")
table = data.sheets()[1]
n_rows = table.nrows
for i in range(n_rows):
    each_row = str(table.row_values(i))
    case_str = case_str + "," + each_row[0]

seg_list = list(jieba.cut(case_str, cut_all=True))
# print "Full Mode:", "/ ".join(seg_list)  # 全模式

list = []
table2 = data.sheets()[0]
n_rows2 = table2.nrows
for j in range(n_rows2):
    if j > 1:
        row = table2.row_values(j)[1]+""
        s_list = jieba.cut(row)
        for k in range(len(s_list)):
            if not s_list[k] in seg_list:
                list.append(table2.row_values(j)[0])



# seg_list = jieba.cut("我来到北京清华大学", cut_all=False)
# print "Default Mode:", "/ ".join(seg_list)  # 精确模式
#
# seg_list = jieba.cut("他来到了网易杭研大厦")  # 默认是精确模式
# print ", ".join(seg_list)
#
# seg_list = jieba.cut_for_search("小明硕士毕业于中国科学院计算所，后在日本京都大学深造")  # 搜索引擎模式
# print ", ".join(seg_list)
