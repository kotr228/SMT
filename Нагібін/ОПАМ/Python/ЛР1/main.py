# Лабораторна робота №1. Варіант 15
# Обчислення значення кусково-заданої функції
import math

a = 2.5
x = float(input("Введіть x: "))

if x > a:
    y = (x + a) ** (1 / 3)
elif x == a:
    y = x * math.cos(a * x)
else:
    y = math.exp(-a * x) * math.sin(a * x)

print("При x =", x, "y =", format(y, ".4f"))
