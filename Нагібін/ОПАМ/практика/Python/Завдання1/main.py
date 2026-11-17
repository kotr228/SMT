# Навчальна практика. Завдання 1. Варіант 15
# Організація аналізу та сортування масивів
from random import randint

n = int(input("Введіть кількість елементів масиву: "))
v = []
for i in range(n):
    v.append(randint(-20, 20))
print("Елементи масиву:")
for i in range(n):
    print(v[i], end=" ")
print()

print("ГОЛОВНЕ МЕНЮ")
print("1 - добуток від'ємних елементів")
print("2 - заміна додатних елементів сумою непарних")
print("3 - вилучення від'ємних елементів")
reg = int(input("Оберіть режим: "))

if reg == 1:
    p = 1
    k = 0
    for i in range(n):
        if v[i] < 0:
            p = p * v[i]
            k = k + 1
    if k > 0:
        print("Добуток від'ємних елементів =", p)
    else:
        print("Від'ємних елементів у масиві немає")
elif reg == 2:
    s = 0
    for i in range(n):
        if v[i] % 2 != 0:
            s = s + v[i]
    for i in range(n):
        if v[i] > 0:
            v[i] = s
    print("Сума непарних елементів =", s)
    print("Елементи масиву після заміни:")
    for i in range(n):
        print(v[i], end=" ")
    print()
elif reg == 3:
    i = 0
    while i < len(v):
        if v[i] < 0:
            v.pop(i)
        else:
            i = i + 1
    print("Масив після вилучення від'ємних елементів:")
    for i in range(len(v)):
        print(v[i], end=" ")
    print()
else:
    print("Такого режиму немає")
