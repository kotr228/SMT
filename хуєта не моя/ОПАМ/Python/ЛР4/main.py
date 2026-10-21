# Лабораторна робота №4. Варіант 15
# У масиві D(30) знайти кількість та добуток додатних елементів
from random import randint

D = []
print("Елементи масиву D:")
for i in range(30):
    D.append(randint(-10, 10))
    print(D[i], end=" ")
print()

k = 0
p = 1
for i in range(30):
    if D[i] > 0:
        k = k + 1
        p = p * D[i]

if k > 0:
    print("Кількість додатних елементів k =", k)
    print("Добуток додатних елементів  p =", p)
else:
    print("Додатних елементів у масиві немає")
