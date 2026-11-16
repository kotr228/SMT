# Лабораторна робота №3. Варіант 15
# Обчислення arth(x) за допомогою ряду: x + x^3/3 + x^5/5 + ...,  |x| < 1
import math

x = float(input("Введіть x (|x| < 1): "))
n = int(input("Введіть кількість членів ряду n: "))

if abs(x) < 1:
    s = 0
    for i in range(n):
        s = s + x ** (2 * i + 1) / (2 * i + 1)
    print("Сума ряду S          =", format(s, ".8f"))
    print("Точне значення arth x =", format(math.atanh(x), ".8f"))
    print("Похибка              =", format(abs(s - math.atanh(x)), ".2e"))
else:
    print("Ряд збігається лише при |x| < 1")
