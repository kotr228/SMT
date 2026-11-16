# Лабораторна робота №2. Варіант 15
# Таблиця значень функції y = ctg(x^4 - 0.55) на [-1; 1] з кроком h = 0.1
import math

x = -1
h = 0.1
print("   x        y")
while x <= 1 + 1e-9:
    y = 1 / math.tan(x ** 4 - 0.55)
    print(format(x, "5.2f"), "  ", format(y, "8.4f"))
    x = round(x + h, 2)
