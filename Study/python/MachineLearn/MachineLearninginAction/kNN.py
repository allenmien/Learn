# coding=utf-8
from numpy import *
import operator


def createDataSet():
    group = array([[1.0, 1.1], [1.0, 1.0], [0, 0], [0, 0.1]])
    labels = ['A', 'A', 'B', 'B']
    return group, labels


def classify0(inX, dataSet, labels, k):
    # 数组长度
    dataSetSize = dataSet.shape[0]
    # （A-0）（B-0）
    diffMat = tile(inX, (dataSetSize, 1)) - dataSet
    # （A-0）*2 （B-0）*2
    sqDiffMat = diffMat ** 2
    # （A - 0）*2 +（B - 0）*2
    sqDistances = sqDiffMat.sum(axis=1)
    distances = sqDistances ** 0.5
    # 得到距离从小到大的索引
    sortedDistIndicies = distances.argsort()
    classCount = {}
    for i in range(k):
        # 获取从小到大的值的标签
        voteIlabel = labels[sortedDistIndicies[i]]
        classCount[voteIlabel] = classCount.get(voteIlabel, 0) + 1
    sortedClassCount = sorted(classCount.iteritems(), key=operator.itemgetter(1), reverse=True)
    return sortedClassCount[0][0]


def file2matrix(filename):
    fr = open(filename)
    numberOfLines = len(fr.readlines())  # get the number of lines in the file
    returnMat = zeros((numberOfLines, 3))  # prepare matrix to return
    classLabelVector = []  # prepare labels return
    fr = open(filename)
    index = 0
    for line in fr.readlines():
        line = line.strip()
        listFromLine = line.split('\t')
        returnMat[index, :] = listFromLine[0:3]
        a = listFromLine[-1]
        classLabelVector.append(int(listFromLine[-1]))
        index += 1
    return returnMat, classLabelVector


datingDataMat, datingLabels = file2matrix("datingTestSet.txt")

group, labels = createDataSet()
classify0([0, 0], group, labels, 3)
