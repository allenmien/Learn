# -*- coding: UTF-8 -*-

a = ['Chr1-10.txt', 'Chr1-1.txt', 'Chr1-2.txt', 'Chr1-14.txt', 'Chr1-3.txt', 'Chr1-20.txt', 'Chr1-5.txt']
b = sorted(a, key=lambda d: int(d.split('-')[-1].split('.')[0]))
print b
